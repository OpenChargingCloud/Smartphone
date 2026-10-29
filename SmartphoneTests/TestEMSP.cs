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
using System.Diagnostics.CodeAnalysis;
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
    /// requests below an account CA, issues charging tickets - checked as the
    /// EMSP checks them, and signed back without the account's signature, with
    /// its ticket issuer's - and countersigns start and stop requests made with
    /// them, as docs/RemoteStartWithChargingTickets.md says, with a token of
    /// each start alone: all of it with the CBOR and COSE of Styx. It starts
    /// nothing anywhere: the charge point operator behind it is what a test
    /// says it reported - a CommandResult, a session, a charge detail record -
    /// and the EMSP tells it, asked for a ticket, as the draft's "After the
    /// start" says.
    /// </summary>
    /// <remarks>
    /// Where told to misbehave, it sends back what a phone has to refuse: a
    /// ticket with the account's signature still in it, or with another
    /// payload; a start countersigned with the token where its signature does
    /// not cover it, by another key than its ticket issuer's, an hour off, or
    /// with the phone's own signature replaced; a stop countersigned with
    /// another token than its start's - or it refuses the start, or knows of
    /// none, as an EMSP from before start requests; or tells of a start with
    /// another token, of a session of another start, or of nothing at all, as
    /// an EMSP from before it told what became of a start.
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
        private readonly ConcurrentDictionary<String, String>                                                       ticketIds    = new();
        private readonly ConcurrentDictionary<String, Byte>                                                         nonces       = new();
        private readonly ConcurrentQueue<StartOrStop>                                                               starts       = new();
        private readonly ConcurrentQueue<StartOrStop>                                                               stops        = new();
        private readonly ConcurrentDictionary<String, StartOrStop>                                                  started      = new();
        private readonly ConcurrentDictionary<String, Reported>                                                     reported     = new();
        private          Int32                                                                                      ticketReads;

        #endregion

        #region Properties

        /// <summary>
        /// Where it is: what a phone is told to sign up at - the port the
        /// operating system gave it as it started.
        /// </summary>
        public String  URL
            => $"http://127.0.0.1:{server.TCPPort}/";

        /// <summary>
        /// How it misbehaves: "keepAccountSignature" or "changePayload" with a
        /// ticket; "takesNoStarts", "refuseStart", "countersignUnprotected",
        /// "countersignWithOtherKey", "countersignAnHourOff" or "replaceOwnSignature"
        /// with a start;
        /// "otherTokenOnStop" with a stop; "reportsNothing", "reportOtherToken",
        /// "reportOtherAuthRef" or "reportsSlowly" - four seconds late - asked
        /// what became of a start; or not at all.
        /// </summary>
        public String? Misbehave  { get; set; }

        /// <summary>
        /// The starts it countersigned, the first first.
        /// </summary>
        public IReadOnlyList<StartOrStop> Starts
            => [ .. starts ];

        /// <summary>
        /// The stops it countersigned, the first first.
        /// </summary>
        public IReadOnlyList<StartOrStop> Stops
            => [ .. stops ];

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
        /// How often somebody asked what became of the starts of a ticket.
        /// </summary>
        public Int32 TicketReads
            => ticketReads;

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
            api.AddHandler(HTTPPath.Parse("/api/v1/tickets/{id}"),              request => emsp.GetTicketAsync(request),                 HTTPMethod.GET);
            api.AddHandler(HTTPPath.Parse("/api/v1/tickets/{id}/start"),        request => Task.FromResult(emsp.PostStart(request)),     HTTPMethod.POST);
            api.AddHandler(HTTPPath.Parse("/api/v1/tickets/{id}/stop"),         request => Task.FromResult(emsp.PostStop(request)),      HTTPMethod.POST);

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

        #region OperatorResult(Token, Result) / OperatorSession(Token, Status, KWh, Cost) / OperatorCDR(Token, KWh, Cost, End)

        /// <summary>
        /// The charge point operator's CommandResult of the start of the given
        /// token - come now, or the given time after the start: anything but
        /// ACCEPTED fails the start, as the EMSP has it.
        /// </summary>
        public void OperatorResult(String Token, String Result, TimeSpan? After = null)
        {

            var start  = reported[Token];
            var at     = After is TimeSpan after ? start.StartedAt + after : DateTimeOffset.UtcNow;

            start.Result = new JObject(
                               new JProperty("result",      Result),
                               new JProperty("message",     new JArray()),
                               new JProperty("receivedAt",  at.ToString("o"))
                           );

            if (Result != "ACCEPTED")
            {
                start.State    = "failed";
                start.EndedAt  = at;
            }

        }

        /// <summary>
        /// The session the charge point operator pushed for the start of the
        /// given token, as OCPI 2.2.1 writes one: its status, what it charged,
        /// and - where said - what that costs with VAT.
        /// </summary>
        public void OperatorSession(String Token, String Status, Decimal KWh, Decimal? Cost = null)
        {

            var start    = reported[Token];
            var session  = new JObject(
                               new JProperty("country_code",             "DE"),
                               new JProperty("party_id",                 "GEF"),
                               new JProperty("id",                       "S-" + Token),
                               new JProperty("start_date_time",          OCPITime(start.StartedAt + TimeSpan.FromSeconds(5))),
                               new JProperty("kwh",                      KWh),
                               new JProperty("cdr_token",                TokenOf(start)),
                               new JProperty("auth_method",              "COMMAND"),
                               new JProperty("authorization_reference",  start.Of.AuthRef),
                               new JProperty("location_id",              "LOC1"),
                               new JProperty("evse_uid",                 "E1"),
                               new JProperty("connector_id",             "1"),
                               new JProperty("currency",                 "EUR"),
                               new JProperty("status",                   Status),
                               new JProperty("last_updated",             OCPITime(DateTimeOffset.UtcNow))
                           );

            if (Cost is Decimal cost)
                session["total_cost"] = PriceOf(cost);

            if (Status == "COMPLETED")
                session["end_date_time"] = OCPITime(DateTimeOffset.UtcNow);

            start.Session = session;

            if (Status is "COMPLETED" or "INVALID" && start.EndedAt is null)
            {
                start.State    = "ended";
                start.EndedAt  = DateTimeOffset.UtcNow;
            }

        }

        /// <summary>
        /// The charge detail record the charge point operator pushed for the
        /// start of the given token, as OCPI 2.2.1 writes one - with signed
        /// meter values, which the phone keeps as they came.
        /// </summary>
        public void OperatorCDR(String Token, Decimal KWh, Decimal Cost, DateTimeOffset End)
        {

            var start = reported[Token];

            start.CDR = new JObject(
                            new JProperty("country_code",             "DE"),
                            new JProperty("party_id",                 "GEF"),
                            new JProperty("id",                       "CDR-" + Token),
                            new JProperty("start_date_time",          OCPITime(start.StartedAt + TimeSpan.FromSeconds(5))),
                            new JProperty("end_date_time",            OCPITime(End)),
                            new JProperty("session_id",               "S-" + Token),
                            new JProperty("cdr_token",                TokenOf(start)),
                            new JProperty("auth_method",              "COMMAND"),
                            new JProperty("authorization_reference",  start.Of.AuthRef),
                            new JProperty("currency",                 "EUR"),
                            new JProperty("total_cost",               PriceOf(Cost)),
                            new JProperty("total_energy",             KWh),
                            new JProperty("total_time",               1.5m),
                            new JProperty("signed_data",              new JObject(
                                new JProperty("encoding_method",  "OCMF"),
                                new JProperty("signed_values",    new JArray(
                                    new JObject(
                                        new JProperty("nature",       "End"),
                                        new JProperty("plain_data",   SignedMeterValue),
                                        new JProperty("signed_data",  "MEUCIQDl0Ev9f7A3")
                                    )
                                ))
                            )),
                            new JProperty("last_updated",             OCPITime(DateTimeOffset.UtcNow))
                        );

        }

        /// <summary>
        /// The meter value its charge detail records carry, as an OCMF text.
        /// </summary>
        public const String SignedMeterValue = """OCMF|{"FV":"1.0","RD":[{"TM":"2026-10-29T13:30:00,000+0000 S","RV":12.5,"RU":"kWh"}]}""";

        private static String OCPITime(DateTimeOffset Time)
            => Time.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

        private static JObject TokenOf(Reported Start)

            => new (
                   new JProperty("country_code",  "DE"),
                   new JProperty("party_id",      "GDF"),
                   new JProperty("uid",           Start.Of.Token),
                   new JProperty("type",          "APP_USER"),
                   new JProperty("contract_id",   Start.Of.Token)
               );

        private static JObject PriceOf(Decimal WithVAT)

            => new (
                   new JProperty("excl_vat",  Math.Round(WithVAT / 1.19m, 2)),
                   new JProperty("incl_vat",  WithVAT)
               );

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
                ticketIds[Convert.ToHexStringLower(Of(payload, CBORValue.FromText("id")).AsBytes())] = user;

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


        #region (private) PostStart(Request) / PostStop(Request)

        /// <summary>
        /// A start request, with the ticket it names: the ticket one this EMSP
        /// issued to whoever asks and good now, the request signed by the
        /// ticket's key alone, near this clock, its nonce new - countersigned,
        /// with a token of this start alone in the protected header.
        /// </summary>
        private HTTPResponse PostStart(HTTPRequest Request)
        {

            if (WhoIs(Request) is not String user)
                return Unauthorized(Request);

            if (Misbehave == "refuseStart")
                return JSON(Request, HTTPStatusCode.Conflict, new JObject(new JProperty("error", "This EMSP is peered with no charge point operator of that EVSE.")));

            // As an EMSP answers that has no such route.
            if (Misbehave == "takesNoStarts")
                return new HTTPResponse.Builder(Request) {
                           HTTPStatusCode  = HTTPStatusCode.NotFound,
                           Connection      = ConnectionType.Close
                       };

            var json = JObject.Parse(Request.HTTPBodyAsUTF8String ?? "{}");

            try
            {

                var ticket   = Convert.FromBase64String(json.Value<String>("ticket")  ?? "");
                var request  = Convert.FromBase64String(json.Value<String>("request") ?? "");

                if (!TryTicketOf(user, Request.ParsedURLParameters.ElementAtOrDefault(0) ?? "", ticket, out var ticketKey, out var refusal))
                    return Refused(Request, refusal);

                using (ticketKey)
                {

                    if (!TryRequestOf(request, StartRequests.StartContentType, StartRequests.StartType, ticket, ticketKey, out var start, out var payload, out refusal))
                        return Refused(Request, refusal);

                    if (payload.TryGetValue(CBORValue.FromText("evse"), out var evse) is false || !evse.TryGetText(out var evseId))
                        return Refused(Request, "A start request names its EVSE.");

                    var token          = "T-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
                    var authRef        = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
                    var countersigned  = Countersign(start, ticketKey, token, authRef);
                    var done           = new StartOrStop(user, request, countersigned, token, authRef);

                    var digest         = Convert.ToHexStringLower(SHA256.HashData(countersigned));

                    starts.Enqueue(done);
                    started[digest] = done;

                    reported[token] = new Reported((Request.ParsedURLParameters.ElementAtOrDefault(0) ?? "").ToLowerInvariant(), digest, done, evseId, DateTimeOffset.UtcNow);

                    return JSON(Request, HTTPStatusCode.OK, new JObject(
                               new JProperty("message",  "Countersigned, and sent to the charge point operator."),
                               new JProperty("request",  Convert.ToBase64String(countersigned)),
                               new JProperty("token",    token),
                               new JProperty("authRef",  authRef)
                           ));

                }

            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or InvalidCastException or CryptographicException)
            {
                return Refused(Request, "No start request with its ticket: " + e.Message);
            }

        }

        /// <summary>
        /// A stop request, with the ticket and the start it names: the start one
        /// this EMSP countersigned for whoever asks, not stopped yet -
        /// countersigned with the token and the reference of that start.
        /// </summary>
        private HTTPResponse PostStop(HTTPRequest Request)
        {

            if (WhoIs(Request) is not String user)
                return Unauthorized(Request);

            var json = JObject.Parse(Request.HTTPBodyAsUTF8String ?? "{}");

            try
            {

                var ticket   = Convert.FromBase64String(json.Value<String>("ticket")  ?? "");
                var start    = Convert.FromBase64String(json.Value<String>("start")   ?? "");
                var request  = Convert.FromBase64String(json.Value<String>("request") ?? "");

                if (!TryTicketOf(user, Request.ParsedURLParameters.ElementAtOrDefault(0) ?? "", ticket, out var ticketKey, out var refusal))
                    return Refused(Request, refusal);

                using (ticketKey)
                {

                    var startDigest = SHA256.HashData(start);

                    if (!started.TryGetValue(Convert.ToHexStringLower(startDigest), out var ofStart) || ofStart.Owner != user)
                        return Refused(Request, "This EMSP countersigned no such start of yours.");

                    if (stops.Any(stop => stop.Token == ofStart.Token))
                        return JSON(Request, HTTPStatusCode.Conflict, new JObject(new JProperty("error", $"The session of {ofStart.Token} is stopped already.")));

                    if (!TryRequestOf(request, StartRequests.StopContentType, StartRequests.StopType, ticket, ticketKey, out var stop, out var payload, out refusal))
                        return Refused(Request, refusal);

                    if (!payload.TryGetValue(CBORValue.FromText("start"), out var namedStart) || !namedStart.TryGetBytes(out var named) || !named.AsSpan().SequenceEqual(startDigest))
                        return Refused(Request, "The stop request names another start than the one it came with.");

                    var token          = Misbehave == "otherTokenOnStop" ? "T-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) : ofStart.Token;
                    var countersigned  = Countersign(stop, ticketKey, token, ofStart.AuthRef);

                    stops.Enqueue(new StartOrStop(user, request, countersigned, token, ofStart.AuthRef));

                    if (reported.TryGetValue(ofStart.Token, out var ofReport) && ofReport.EndedAt is null)
                    {
                        ofReport.State    = "stopped";
                        ofReport.EndedAt  = DateTimeOffset.UtcNow;
                    }

                    return JSON(Request, HTTPStatusCode.OK, new JObject(
                               new JProperty("message",  "Countersigned, and sent to the charge point operator."),
                               new JProperty("request",  Convert.ToBase64String(countersigned))
                           ));

                }

            }
            catch (Exception e) when (e is FormatException or InvalidOperationException or InvalidCastException or CryptographicException)
            {
                return Refused(Request, "No stop request with its ticket and its start: " + e.Message);
            }

        }

        #endregion

        #region (private) GetTicketAsync(Request)

        /// <summary>
        /// What became of the starts made with a ticket this EMSP issued to
        /// whoever asks: its state of each, and what the charge point operator
        /// reported of it - 404 for a ticket of another, as for none.
        /// </summary>
        private async Task<HTTPResponse> GetTicketAsync(HTTPRequest Request)
        {

            if (WhoIs(Request) is not String user)
                return Unauthorized(Request);

            Interlocked.Increment(ref ticketReads);

            if (Misbehave == "reportsSlowly")
                await Task.Delay(TimeSpan.FromSeconds(4));

            // As an EMSP answers that has no such route.
            if (Misbehave == "reportsNothing")
                return new HTTPResponse.Builder(Request) {
                           HTTPStatusCode  = HTTPStatusCode.NotFound,
                           Connection      = ConnectionType.Close
                       };

            var id = (Request.ParsedURLParameters.ElementAtOrDefault(0) ?? "").ToLowerInvariant();

            if (!ticketIds.TryGetValue(id, out var owner) || owner != user)
                return JSON(Request, HTTPStatusCode.NotFound, new JObject(new JProperty("error", $"This EMSP issued you no ticket '{id}'.")));

            return JSON(Request, HTTPStatusCode.OK, new JObject(
                       new JProperty("id",      id),
                       new JProperty("starts",  new JArray(reported.Values.
                                                               Where            (start => start.TicketId == id).
                                                               OrderByDescending(start => start.StartedAt).
                                                               Select           (start => start.ToJSON(Misbehave))))
                   ));

        }

        #endregion

        #region (private) TryTicketOf(User, Id, Ticket, out TicketKey, out Refusal)

        /// <summary>
        /// The key of a ticket a request came with: one this EMSP signed as its
        /// ticket issuer, issued to the given user by the given id, and good now.
        /// </summary>
        private Boolean TryTicketOf(String                                   User,
                                    String                                   Id,
                                    Byte[]                                   Ticket,
                                    [NotNullWhen(true)]  out ECDsa?          TicketKey,
                                    [NotNullWhen(false)] out String?         Refusal)
        {

            TicketKey = null;

            if (!COSESign.TryParse(Ticket, out var ticket, out var error))
            {
                Refusal = "The ticket is no COSE_Sign: " + error;
                return false;
            }

            if (!ticket.Signatures.Any(signature => ticket.Verify(signature, ChargingTickets.PublicKeyOf(issuerKey), out _, ExpectedAlgorithm: COSEAlgorithm.ES256)))
            {
                Refusal = "The ticket is not signed by this EMSP's ticket issuer.";
                return false;
            }

            var payload = CBORValue.Parse(ticket.Payload ?? []);

            if (!payload.TryGetValue(CBORValue.FromText("id"), out var id) || Convert.ToHexStringLower(id.AsBytes()) != Id.ToLowerInvariant() ||
                !ticketIds.TryGetValue(Id.ToLowerInvariant(), out var owner) || owner != User)
            {
                Refusal = $"This EMSP issued you no ticket '{Id}'.";
                return false;
            }

            if (!payload.TryGetValue(CBORValue.FromText("exp"), out var exp) || exp.AsInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                Refusal = $"The ticket '{Id}' is good no more.";
                return false;
            }

            payload.TryGetValue(CBORValue.FromText("key"), out var key);
            key.TryGetValue(CBORValue.FromInt64(-2), out var x);
            key.TryGetValue(CBORValue.FromInt64(-3), out var y);

            TicketKey = ECDsa.Create(new ECParameters {
                            Curve  = ECCurve.NamedCurves.nistP256,
                            Q      = new ECPoint { X = x.AsBytes(), Y = y.AsBytes() }
                        });

            Refusal = null;
            return true;

        }

        #endregion

        #region (private) TryRequestOf(Bytes, ContentType, Type, Ticket, TicketKey, out Request, out Payload, out Refusal)

        /// <summary>
        /// A start or a stop request: of its content type and type, naming the
        /// ticket it came with, signed by the ticket's key alone, near this
        /// clock, and with a nonce never seen before.
        /// </summary>
        private Boolean TryRequestOf(Byte[]                                    Bytes,
                                     String                                    ContentType,
                                     String                                    Type,
                                     Byte[]                                    Ticket,
                                     ECDsa                                     TicketKey,
                                     [NotNullWhen(true)]  out COSESign?        Request,
                                     out CBORValue                             Payload,
                                     [NotNullWhen(false)] out String?          Refusal)
        {

            Payload = CBORValue.Null;

            if (!COSESign.TryParse(Bytes, out Request, out var error))
            {
                Refusal = "The request is no COSE_Sign: " + error;
                return false;
            }

            if (Request.ProtectedHeader.ContentType?.TryGetText(out var contentType) != true || contentType != ContentType)
            {
                Refusal = $"The body's protected header says it is no '{ContentType}'.";
                return false;
            }

            Payload = CBORValue.Parse(Request.Payload ?? []);

            if (!Payload.TryGetValue(CBORValue.FromText("typ"), out var typ) || typ.AsText() != Type ||
                !Payload.TryGetValue(CBORValue.FromText("v"),   out var v)   || v.AsInt64()  != StartRequests.Version)
            {
                Refusal = $"It is no {Type} of version 1.";
                return false;
            }

            if (!Payload.TryGetValue(CBORValue.FromText("ticket"), out var named) || !named.AsBytes().AsSpan().SequenceEqual(SHA256.HashData(Ticket)))
            {
                Refusal = "The request names another ticket than the one it came with.";
                return false;
            }

            if (Request.Signatures.Count != 1 || Request.Signatures[0].ProtectedHeader.KeyIdentifier?.AsSpan().SequenceEqual(ChargingTickets.TicketKeyId) != true ||
               !Request.Verify(Request.Signatures[0], ChargingTickets.PublicKeyOf(TicketKey), out _, ExpectedAlgorithm: COSEAlgorithm.ES256))
            {
                Refusal = "The request is not signed by the ticket's key, and by nobody else.";
                return false;
            }

            if (!Payload.TryGetValue(CBORValue.FromText("at"), out var at) || Math.Abs(at.AsInt64() - DateTimeOffset.UtcNow.ToUnixTimeSeconds()) > 300)
            {
                Refusal = "The request was made more than five minutes from now.";
                return false;
            }

            if (!Payload.TryGetValue(CBORValue.FromText("nonce"), out var nonce) || nonce.AsBytes().Length != 16 || !nonces.TryAdd(Convert.ToHexString(nonce.AsBytes()), 0))
            {
                Refusal = "The request's nonce is no 16 bytes, or was seen before.";
                return false;
            }

            Refusal = null;
            return true;

        }

        #endregion

        #region (private) Countersign(Request, TicketKey, Token, AuthRef)

        /// <summary>
        /// The request with this EMSP's signature added, the token, the
        /// reference and the time in its protected header - or as told to
        /// misbehave.
        /// </summary>
        private Byte[] Countersign(COSESign  Request,
                                   ECDsa     TicketKey,
                                   String    Token,
                                   String    AuthRef)
        {

            var issuerKid  = SHA256.HashData(issuer.RawData);
            var now        = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + (Misbehave == "countersignAnHourOff" ? 3600 : 0);
            var signed     = COSEHeaders.Create(COSEAlgorithm.ES256, issuerKid).
                                 Set(CBORValue.FromText(StartRequests.TokenLabel),     CBORValue.FromText(Token)).
                                 Set(CBORValue.FromText(StartRequests.AuthRefLabel),   CBORValue.FromText(AuthRef)).
                                 Set(CBORValue.FromText(StartRequests.SignedAtLabel),  CBORValue.FromInt64(now));

            using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);

            switch (Misbehave)
            {

                case "countersignUnprotected":
                    return Request.AddSignature(ChargingTickets.PrivateKeyOf(issuerKey),
                                                COSEHeaders.Create(COSEAlgorithm.ES256, issuerKid).
                                                    Set(CBORValue.FromText(StartRequests.SignedAtLabel), CBORValue.FromInt64(now)),
                                                COSEHeaders.Empty.
                                                    Set(CBORValue.FromText(StartRequests.TokenLabel),    CBORValue.FromText(Token)).
                                                    Set(CBORValue.FromText(StartRequests.AuthRefLabel),  CBORValue.FromText(AuthRef))).
                                   ToByteArray();

                case "countersignWithOtherKey":
                    return Request.AddSignature(ChargingTickets.PrivateKeyOf(otherKey), signed).ToByteArray();

                case "replaceOwnSignature":
                    return COSESign.Sign(Request.Payload!,
                                         ChargingTickets.PrivateKeyOf(otherKey),
                                         COSEHeaders.Create(COSEAlgorithm.ES256, ChargingTickets.TicketKeyId),
                                         BodyProtectedHeader:  Request.ProtectedHeader,
                                         CanonicalizePayload:  false).
                                   AddSignature(ChargingTickets.PrivateKeyOf(issuerKey), signed).
                                   ToByteArray();

                default:
                    return Request.AddSignature(ChargingTickets.PrivateKeyOf(issuerKey), signed).ToByteArray();

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


    /// <summary>
    /// A start or a stop the test EMSP countersigned: for whom, the request as
    /// it came, as it went, and the token and the reference of its start.
    /// </summary>
    public sealed record StartOrStop(String  Owner,
                                     Byte[]  Request,
                                     Byte[]  Countersigned,
                                     String  Token,
                                     String  AuthRef);


    /// <summary>
    /// A start the test EMSP countersigned, and what became of it as the EMSP
    /// tells it: its state, and what the charge point operator reported.
    /// </summary>
    internal sealed class Reported(String          TicketId,
                                   String          Start,
                                   StartOrStop     Of,
                                   String          EVSE,
                                   DateTimeOffset  StartedAt)
    {

        public String           TicketId    { get; }      = TicketId;
        public String           Start       { get; }      = Start;
        public StartOrStop      Of          { get; }      = Of;
        public String           EVSE        { get; }      = EVSE;
        public DateTimeOffset   StartedAt   { get; }      = StartedAt;
        public String           State       { get; set; } = "started";
        public DateTimeOffset?  EndedAt     { get; set; }
        public JObject?         Result      { get; set; }
        public JObject?         Session     { get; set; }
        public JObject?         CDR         { get; set; }

        /// <summary>
        /// The start as GET /api/v1/tickets/{id} tells it - with another token,
        /// or its session of another start, where told to misbehave.
        /// </summary>
        public JObject ToJSON(String? Misbehave)
        {

            var json = new JObject(
                           new JProperty("start",      Start),
                           new JProperty("token",      Misbehave == "reportOtherToken" ? "T-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(8)) : Of.Token),
                           new JProperty("authRef",    Of.AuthRef),
                           new JProperty("evse",       EVSE),
                           new JProperty("state",      State),
                           new JProperty("startedAt",  StartedAt.ToString("o")),
                           new JProperty("endedAt",    EndedAt?.ToString("o"))
                       );

            if (Result is not null)
                json["result"] = Result.DeepClone();

            if (Session is not null)
            {

                var session = (JObject) Session.DeepClone();

                if (Misbehave == "reportOtherAuthRef")
                    session["authorization_reference"] = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

                json["session"] = session;

            }

            if (CDR is not null)
                json["cdr"] = CDR.DeepClone();

            return json;

        }

    }

}
