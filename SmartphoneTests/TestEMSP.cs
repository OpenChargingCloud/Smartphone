/*
 * Copyright (c) 2014-2026 GraphDefined GmbH <achim.friedland@graphdefined.com>
 * This file is part of Smartphone <https://github.com/OpenChargingCloud/Smartphone>
 *
 * Licensed under the Affero GPL license, Version 3.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *     http://www.gnu.org/licenses/agpl.html
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

#region Usings

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.Smartphone.Provider;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// An e-mobility provider on the loopback, as far as a phone asks one: it
    /// signs accounts up and in - with a session in cookies, as Hermod's
    /// HTTPExtAPI does, and no HTTP Basic - certifies account keys from PKCS#10
    /// requests below an account CA, and issues charging tickets - checked as
    /// the EMSP checks them, and signed back without the account's signature,
    /// with its ticket issuer's - all of it with the CBOR and COSE of Styx.
    /// </summary>
    /// <remarks>
    /// Where told to misbehave, it sends back tickets a phone has to refuse:
    /// with the account's signature still in them, or with another payload.
    /// </remarks>
    public sealed class TestEMSP : IAsyncDisposable
    {

        #region Data

        public const String  Party  = "DE*GDF";

        private readonly HTTPServer                                       server;
        private readonly ECDsa                                            accountCAKey  = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly X509Certificate2                                 accountCA;
        private readonly ECDsa                                            issuerKey     = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly X509Certificate2                                 issuer;

        private readonly ConcurrentDictionary<String, String>             passwords     = new();
        private readonly ConcurrentDictionary<String, String>             sessions      = new();
        private          Int32                                            signIns;
        private readonly ConcurrentDictionary<String, (String Owner, X509Certificate2 Certificate, Boolean Revoked)>  accountKeys  = new();
        private readonly ConcurrentQueue<(String Owner, Byte[] Request, Byte[] Issued)>                             tickets      = new();

        #endregion

        #region Properties

        /// <summary>
        /// Where it is: what a phone is told to sign up at - the port the
        /// operating system gave it as it started.
        /// </summary>
        public String  URL
            => $"http://127.0.0.1:{server.TCPPort}/";

        /// <summary>
        /// How it misbehaves: "keepAccountSignature" or "changePayload", or not at all.
        /// </summary>
        public String? Misbehave  { get; set; }

        /// <summary>
        /// The tickets it issued: to whom, the request as it came, the ticket as it went.
        /// </summary>
        public IReadOnlyList<(String Owner, Byte[] Request, Byte[] Issued)> Tickets
            => [ .. tickets ];

        /// <summary>
        /// The account keys it certified: their ids, to whom, and whether taken back.
        /// </summary>
        public IReadOnlyDictionary<String, (String Owner, X509Certificate2 Certificate, Boolean Revoked)> AccountKeys
            => accountKeys;

        /// <summary>
        /// How often somebody signed in, by sign-up or sign-in.
        /// </summary>
        public Int32 SignIns
            => signIns;

        /// <summary>
        /// The certificate of its ticket issuer.
        /// </summary>
        public X509Certificate2 Issuer
            => issuer;

        #endregion

        #region Constructor(s)

        private TestEMSP(HTTPServer Server)
        {

            this.server     = Server;

            this.accountCA  = new CertificateRequest("CN=Account CA of the test EMSP", accountCAKey, HashAlgorithmName.SHA256) {
                                  CertificateExtensions = { new X509BasicConstraintsExtension(true, false, 0, true) }
                              }.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

            this.issuer     = new CertificateRequest("CN=Ticket issuer of the test EMSP", issuerKey, HashAlgorithmName.SHA256).
                                  CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(5));

        }

        #endregion


        #region (static) Start()

        public static async Task<TestEMSP> Start()
        {

            var server  = new HTTPServer(IPAddress: IPv4Address.Localhost, TCPPort: IPPort.Zero);
            var emsp    = new TestEMSP(server);
            var api     = server.AddHTTPAPI(HTTPPath.Root);

            api.AddHandler(HTTPPath.Parse("/ext/auth/signup"),                  request => Task.FromResult(emsp.SignUp(request)),        HTTPMethod.POST);
            api.AddHandler(HTTPPath.Parse("/ext/auth/login"),                   request => Task.FromResult(emsp.SignIn(request)),        HTTPMethod.POST);
            api.AddHandler(HTTPPath.Parse("/api/v1/tickets"),                   request => Task.FromResult(emsp.GetTickets(request)),    HTTPMethod.GET);
            api.AddHandler(HTTPPath.Parse("/api/v1/tickets"),                   request => Task.FromResult(emsp.PostTicket(request)),    HTTPMethod.POST);
            api.AddHandler(HTTPPath.Parse("/api/v1/account-keys"),              request => Task.FromResult(emsp.PostAccountKey(request)), HTTPMethod.POST);
            api.AddHandler(HTTPPath.Parse("/api/v1/account-keys/{id}/revoke"),  request => Task.FromResult(emsp.RevokeKey(request)),     HTTPMethod.POST);

            await server.Start();

            return emsp;

        }

        #endregion

        #region SignedUp(Username, Password) / EndSessions()

        /// <summary>
        /// An account made at it beforehand, as a driver signs up in a browser.
        /// </summary>
        public void SignedUp(String Username, String Password)

            => passwords[Username] = Password;

        /// <summary>
        /// Every session over, as after a restart of the provider.
        /// </summary>
        public void EndSessions()

            => sessions.Clear();

        #endregion


        #region (private) SignUp(Request)

        private HTTPResponse SignUp(HTTPRequest Request)
        {

            var json      = JObject.Parse(Request.HTTPBodyAsUTF8String ?? "{}");
            var username  = json.Value<String>("username") ?? "";
            var password  = json.Value<String>("password") ?? "";

            if (username.Length == 0 || password.Length < 12 || json.Value<String>("email") is not { Length: > 0 })
                return JSON(Request, HTTPStatusCode.BadRequest, new JObject(new JProperty("error", "A name, an e-mail address and a password of 12 characters at least.")));

            if (!passwords.TryAdd(username, password))
                return JSON(Request, HTTPStatusCode.Conflict, new JObject(new JProperty("error", $"The username '{username}' is taken.")));

            return SignedIn(Request, HTTPStatusCode.Created, username);

        }

        #endregion

        #region (private) SignIn(Request) / SignedIn(Request, Status, User)

        private HTTPResponse SignIn(HTTPRequest Request)
        {

            var json      = JObject.Parse(Request.HTTPBodyAsUTF8String ?? "{}");
            var login     = json.Value<String>("login")    ?? "";
            var password  = json.Value<String>("password") ?? "";

            return passwords.TryGetValue(login, out var kept) && kept == password
                       ? SignedIn(Request, HTTPStatusCode.OK, login)
                       : JSON(Request, HTTPStatusCode.Unauthorized, new JObject(new JProperty("description", "Unknown login or wrong password.")));

        }

        /// <summary>
        /// A session, handed out as Hermod's HTTPExtAPI hands one out: two
        /// cookies, the second the session's token.
        /// </summary>
        private HTTPResponse SignedIn(HTTPRequest Request, HTTPStatusCode Status, String User)
        {

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

            sessions[token] = User;
            Interlocked.Increment(ref signIns);

            return new HTTPResponse.Builder(Request) {
                       HTTPStatusCode  = Status,
                       ContentType     = HTTPContentType.Application.JSON_UTF8,
                       Content         = Encoding.UTF8.GetBytes(new JObject(new JProperty("username", User)).ToString()),
                       SetCookie       = HTTPCookies.Parse($"emsp=login={User}; Path=/",
                                                           $"emspSession={token}; Path=/; HttpOnly"),
                       Connection      = ConnectionType.Close
                   };

        }

        #endregion

        #region (private) GetTickets(Request)

        private HTTPResponse GetTickets(HTTPRequest Request)
        {

            if (WhoIs(Request) is null)
                return Unauthorized(Request);

            return JSON(Request, HTTPStatusCode.OK, new JObject(
                       new JProperty("party",            Party),
                       new JProperty("maxValidityDays",  30),
                       new JProperty("issuer",           new JObject(new JProperty("pem", issuer.ExportCertificatePem()))),
                       new JProperty("tickets",          new JArray())
                   ));

        }

        #endregion

        #region (private) PostAccountKey(Request) / RevokeKey(Request)

        private HTTPResponse PostAccountKey(HTTPRequest Request)
        {

            if (WhoIs(Request) is not String user)
                return Unauthorized(Request);

            var json = JObject.Parse(Request.HTTPBodyAsUTF8String ?? "{}");

            CertificateRequest csr;

            try
            {
                csr = CertificateRequest.LoadSigningRequestPem(json.Value<String>("csr") ?? "", HashAlgorithmName.SHA256);
            }
            catch (CryptographicException e)
            {
                return JSON(Request, HTTPStatusCode.BadRequest, new JObject(new JProperty("error", "No PKCS#10 request: " + e.Message)));
            }

            using var key = csr.PublicKey.GetECDsaPublicKey();

            if (key is null || key.KeySize != 256)
                return JSON(Request, HTTPStatusCode.BadRequest, new JObject(new JProperty("error", "An account key is on P-256.")));

            var certified    = new CertificateRequest(new X500DistinguishedName($"CN={user}"), csr.PublicKey, HashAlgorithmName.SHA256) {
                                   CertificateExtensions = { new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true) }
                               }.Create(accountCA, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddYears(2), RandomNumberGenerator.GetBytes(16));

            var id           = Convert.ToHexString(SHA256.HashData(certified.RawData));

            accountKeys[id]  = (user, certified, false);

            return JSON(Request, HTTPStatusCode.Created, new JObject(
                       new JProperty("message",             "Certified."),
                       new JProperty("accountCertificate",  new JObject(new JProperty("id", id))),
                       new JProperty("certificate",         certified.ExportCertificatePem()),
                       new JProperty("ca",                  accountCA.ExportCertificatePem())
                   ));

        }

        private HTTPResponse RevokeKey(HTTPRequest Request)
        {

            if (WhoIs(Request) is not String user)
                return Unauthorized(Request);

            var id = Request.ParsedURLParameters.ElementAtOrDefault(0)?.ToUpperInvariant() ?? "";

            if (!accountKeys.TryGetValue(id, out var key) || key.Owner != user)
                return JSON(Request, HTTPStatusCode.NotFound, new JObject(new JProperty("error", $"No account key '{id}' of yours.")));

            accountKeys[id] = key with { Revoked = true };

            return JSON(Request, HTTPStatusCode.OK, new JObject(new JProperty("message", "Taken back.")));

        }

        #endregion

        #region (private) PostTicket(Request)

        private HTTPResponse PostTicket(HTTPRequest Request)
        {

            if (WhoIs(Request) is not String user)
                return Unauthorized(Request);

            var bytes = Convert.FromBase64String(JObject.Parse(Request.HTTPBodyAsUTF8String ?? "{}").Value<String>("request") ?? "");

            if (!COSESign.TryParse(bytes, out var request, out var error))
                return Refused(Request, "No COSE_Sign: " + error);

            if (request.ProtectedHeader.ContentType?.TryGetText(out var contentType) != true || contentType != ChargingTickets.ContentType)
                return Refused(Request, $"The body's protected header says it is no '{ChargingTickets.ContentType}'.");

            var payload = CBORValue.Parse(request.Payload ?? []);

            CBORValue Of(CBORValue Map, CBORValue Key) => Map.TryGetValue(Key, out var value) ? value : throw new FormatException($"'{Key}' is missing.");

            try
            {

                if (Of(payload, CBORValue.FromText("typ")).AsText() != ChargingTickets.Type || Of(payload, CBORValue.FromText("v")).AsInt64() != ChargingTickets.Version)
                    return Refused(Request, "It is no charging ticket of version 1.");

                if (Of(payload, CBORValue.FromText("id")).AsBytes().Length != 16)
                    return Refused(Request, "A ticket's id is 16 bytes.");

                if (Of(payload, CBORValue.FromText("emsp")).AsText() != Party)
                    return Refused(Request, $"The ticket is not for {Party}.");

                var nbf = Of(payload, CBORValue.FromText("nbf")).AsInt64();
                var exp = Of(payload, CBORValue.FromText("exp")).AsInt64();

                if (exp <= nbf || exp <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() || exp - nbf > 30 * 86400)
                    return Refused(Request, "The ticket is not good for a time this EMSP signs.");

                var key = Of(payload, CBORValue.FromText("key"));

                using var ticketKey = ECDsa.Create(new ECParameters {
                                          Curve  = ECCurve.NamedCurves.nistP256,
                                          Q      = new ECPoint { X = Of(key, CBORValue.FromInt64(-2)).AsBytes(), Y = Of(key, CBORValue.FromInt64(-3)).AsBytes() }
                                      });

                if (request.Signatures.Count != 2)
                    return Refused(Request, "A ticket comes signed twice.");

                var own      = request.Signatures.FirstOrDefault(signature => signature.ProtectedHeader.KeyIdentifier?.AsSpan().SequenceEqual(ChargingTickets.TicketKeyId) == true);
                var account  = request.Signatures.FirstOrDefault(signature => !ReferenceEquals(signature, own));

                if (own is null || account is null)
                    return Refused(Request, "A ticket comes signed by its key (kid \"ticket\", protected) and by an account key.");

                if (!request.Verify(own, ChargingTickets.PublicKeyOf(ticketKey), out _, ExpectedAlgorithm: COSEAlgorithm.ES256) || own.Signature.Length != 64)
                    return Refused(Request, "The ticket's own signature is not by its key.");

                var kid = account.ProtectedHeader.KeyIdentifier;

                if (kid is not { Length: 32 } || !accountKeys.TryGetValue(Convert.ToHexString(kid), out var accountKey) || accountKey.Owner != user || accountKey.Revoked)
                    return Refused(Request, "The ticket is not signed by a key of yours.");

                using var certified = accountKey.Certificate.GetECDsaPublicKey()!;

                if (!request.Verify(account, ChargingTickets.PublicKeyOf(certified), out _, ExpectedAlgorithm: COSEAlgorithm.ES256) || account.Signature.Length != 64)
                    return Refused(Request, "The account's signature is not by its key.");

                var issuerKid  = SHA256.HashData(issuer.RawData);
                var keep       = Misbehave == "keepAccountSignature" ? new[] { own, account } : new[] { own };
                var issuePayload = Misbehave == "changePayload"
                                       ? CBORValue.FromMap([ .. payload.AsMap().Select(entry => entry.Key.AsText() == "exp"
                                                                                                    ? new KeyValuePair<CBORValue, CBORValue>(entry.Key, CBORValue.FromInt64(exp + 86400))
                                                                                                    : entry) ]).ToByteArray(CBORWriterOptions.Canonical)
                                       : request.Payload!;

                var issued     = new COSESign(request.ProtectedHeaderBytes, request.UnprotectedHeader, issuePayload, keep, true).
                                     AddSignature(ChargingTickets.PrivateKeyOf(issuerKey), COSEHeaders.Create(COSEAlgorithm.ES256, issuerKid)).
                                     ToByteArray();

                tickets.Enqueue((user, bytes, issued));

                return JSON(Request, HTTPStatusCode.Created, new JObject(
                           new JProperty("message",  "Signed."),
                           new JProperty("ticket",   Convert.ToBase64String(issued)),
                           new JProperty("issuer",   Convert.ToHexStringLower(issuerKid))
                       ));

            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or InvalidCastException or CryptographicException)
            {
                return Refused(Request, "It is no charging ticket: " + e.Message);
            }

        }

        #endregion


        #region (private) WhoIs(Request)

        /// <summary>
        /// Whose session the request carries - and nobody for HTTP Basic, which
        /// an EMSP does not take for an account that signed itself up.
        /// </summary>
        private String? WhoIs(HTTPRequest Request)
        {

            var match = System.Text.RegularExpressions.Regex.Match(Request.GetHeaderField("Cookie") ?? "", @"emspSession=([0-9A-F]+)");

            return match.Success && sessions.TryGetValue(match.Groups[1].Value, out var user)
                       ? user
                       : null;

        }

        #endregion

        #region (private static) JSON(Request, Status, Data) / Refused(Request, Error) / Unauthorized(Request)

        private static HTTPResponse JSON(HTTPRequest Request, HTTPStatusCode Status, JObject Data)

            => new HTTPResponse.Builder(Request) {
                   HTTPStatusCode  = Status,
                   ContentType     = HTTPContentType.Application.JSON_UTF8,
                   Content         = Encoding.UTF8.GetBytes(Data.ToString()),
                   Connection      = ConnectionType.Close
               };

        private static HTTPResponse Refused(HTTPRequest Request, String Error)
            => JSON(Request, HTTPStatusCode.BadRequest, new JObject(new JProperty("error", Error)));

        private static HTTPResponse Unauthorized(HTTPRequest Request)
            => JSON(Request, HTTPStatusCode.Unauthorized, new JObject(new JProperty("error", "Sign in.")));

        #endregion


        public async ValueTask DisposeAsync()
        {
            await server.DisposeAsync();
            accountCAKey.Dispose();
            issuerKey.Dispose();
        }

    }

}
