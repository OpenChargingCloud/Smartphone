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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.WWCP.Node;

using cloud.charging.open.Smartphone.Provider;

#endregion

namespace cloud.charging.open.Smartphone
{

    /// <summary>
    /// What came of asking the e-mobility provider: whether it worked, what to
    /// say about it - and whether only the phone's own files were at fault.
    /// </summary>
    public sealed record ProviderResult(Boolean  Success,
                                        String   Message,
                                        Boolean  NotSaved    = false,
                                        Boolean  Upstream    = false);


    public partial class Smartphone
    {

        #region Data

        /// <summary>
        /// The name of the directory beside the configuration file the account
        /// at the e-mobility provider and the tickets it issued are kept in.
        /// </summary>
        public const String  ProviderDirectoryName  = "provider";

        private readonly SemaphoreSlim     providerLock  = new (1, 1);
        private          ProviderAccount?  provider;

        /// <summary>
        /// The provider's client of the account, with its session.
        /// </summary>
        private          EMSPClient?       providerClient;

        #endregion

        #region Properties

        /// <summary>
        /// Where the account at the e-mobility provider and its tickets are kept:
        /// a directory nobody but the account the phone runs under may read.
        /// </summary>
        public String            ProviderPath      { get; }

        /// <summary>
        /// The account of this phone at its e-mobility provider, if it has one.
        /// </summary>
        public ProviderAccount?  ProviderAccount
            => provider;

        private String ProviderAccountFile
            => Path.Combine(ProviderPath, "account.json");

        private String ProviderTicketsPath
            => Path.Combine(ProviderPath, "tickets");

        #endregion


        #region (private) LoadProviderAccount()

        /// <summary>
        /// The account the phone had at the start - none where it had none, or
        /// where its file could not be read, which the log says.
        /// </summary>
        private ProviderAccount? LoadProviderAccount()
        {

            OwnerOnly.Directory(ProviderPath, problem => Log.Warning($"The account at the e-mobility provider is kept in '{ProviderPath}', which could not be closed to everybody else: {problem}", "provider"));

            if (!File.Exists(ProviderAccountFile))
                return null;

            try
            {

                if (ProviderAccount.TryParseStorage(JObject.Parse(File.ReadAllText(ProviderAccountFile)), out var account, out var error))
                    return account;

                Log.Warning(error, "provider");

            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                Log.Warning($"The account at the e-mobility provider in '{ProviderAccountFile}' could not be read: {e.Message}", "provider");
            }

            return null;

        }

        #endregion


        #region SignUpAtProviderAsync(URL, Username, EMail, Who)

        /// <summary>
        /// Make an account at an e-mobility provider for this phone - with a
        /// password the phone makes up and keeps, which nobody has to type -
        /// and an account key of the phone, certified by the provider.
        /// </summary>
        /// <param name="URL">Where the provider is.</param>
        /// <param name="Username">The name of the account.</param>
        /// <param name="EMail">The e-mail address of the account.</param>
        /// <param name="Who">Who asked, for the log.</param>
        public async Task<ProviderResult> SignUpAtProviderAsync(String  URL,
                                                                String  Username,
                                                                String  EMail,
                                                                String  Who)
        {

            if (!IsProviderURL(URL, out var problem))
                return new ProviderResult(false, problem);

            if (String.IsNullOrWhiteSpace(Username) || String.IsNullOrWhiteSpace(EMail))
                return new ProviderResult(false, "An account at an e-mobility provider has a name and an e-mail address.");

            await providerLock.WaitAsync();

            try
            {

                if (provider is not null)
                    return new ProviderResult(false, $"This phone has an account at {provider.URL} already, '{provider.Username}': forget it first.");

                // Made up and kept: the phone signs in with it, and nobody else
                // has to know it.
                var password  = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)).Replace('+', '-').Replace('/', '_');
                var client    = new EMSPClient(URL, DNSClient);
                var signedUp  = await client.SignUpAsync(Username.Trim(), EMail.Trim(), password, Username.Trim());

                if (!signedUp.Success)
                    return new ProviderResult(false, $"The e-mobility provider at {URL} made no account '{Username.Trim()}': {signedUp.Problem}", Upstream: signedUp.Status == 0);

                return await ConnectAsync(client, URL, Username.Trim(), EMail.Trim(), password, MadeUp: true, Who);

            }
            finally
            {
                providerLock.Release();
            }

        }

        #endregion

        #region SignInAtProviderAsync(URL, Username, Password, Who)

        /// <summary>
        /// Take an account the driver has at an e-mobility provider already -
        /// its name and password are kept by the phone from now on - and give
        /// it an account key of this phone.
        /// </summary>
        public async Task<ProviderResult> SignInAtProviderAsync(String  URL,
                                                                String  Username,
                                                                String  Password,
                                                                String  Who)
        {

            if (!IsProviderURL(URL, out var problem))
                return new ProviderResult(false, problem);

            if (String.IsNullOrWhiteSpace(Username) || String.IsNullOrEmpty(Password))
                return new ProviderResult(false, "Signing in at an e-mobility provider takes the name of the account and its password.");

            await providerLock.WaitAsync();

            try
            {

                if (provider is not null)
                    return new ProviderResult(false, $"This phone has an account at {provider.URL} already, '{provider.Username}': forget it first.");

                return await ConnectAsync(new EMSPClient(URL, DNSClient, Username.Trim(), Password), URL, Username.Trim(), "", Password, MadeUp: false, Who);

            }
            finally
            {
                providerLock.Release();
            }

        }

        #endregion

        #region (private) ConnectAsync(Client, URL, Username, EMail, Password, MadeUp, Who)

        /// <summary>
        /// Signed in: the provider's name for itself and its ticket issuer, an
        /// account key of the phone certified by the provider, and all of it
        /// kept.
        /// </summary>
        private async Task<ProviderResult> ConnectAsync(EMSPClient  Client,
                                                        String      URL,
                                                        String      Username,
                                                        String      EMail,
                                                        String      Password,
                                                        Boolean     MadeUp,
                                                        String      Who)
        {

            var tickets = await Client.TicketsAsync();

            if (!tickets.Success)
                return new ProviderResult(false, $"The e-mobility provider at {URL} did not let '{Username}' in: {tickets.Problem}", Upstream: tickets.Status == 0);

            var party      = tickets.JSON?["party"]?.Value<String>();
            var issuerPEM  = tickets.JSON?["issuer"]?["pem"]?.Value<String>();

            if (String.IsNullOrEmpty(party) || String.IsNullOrEmpty(issuerPEM))
                return new ProviderResult(false, $"The e-mobility provider at {URL} says nothing of its charging tickets: no name of its own, or no ticket issuer. Is it one?", Upstream: true);

            // The phone's account key: made here, and never anywhere else. The
            // provider gets a signing request and takes nothing from it but the
            // key, which the request proves the phone holds.
            using var accountKey  = ChargingTickets.NewKey();
            var csr               = new CertificateRequest("CN=" + Environment.MachineName, accountKey, HashAlgorithmName.SHA256).CreateSigningRequestPem();
            var certified         = await Client.AccountKeyAsync(csr, $"Smartphone {Environment.MachineName}");

            if (!certified.Success)
                return new ProviderResult(false, $"The e-mobility provider at {URL} certified no account key of this phone: {certified.Problem}", Upstream: certified.Status == 0);

            var certificatePEM  = certified.JSON?["certificate"]?.Value<String>();
            var certificateId   = certified.JSON?["accountCertificate"]?["id"]?.Value<String>();

            if (String.IsNullOrEmpty(certificatePEM) || String.IsNullOrEmpty(certificateId))
                return new ProviderResult(false, $"The e-mobility provider at {URL} answered the account key with no certificate.", Upstream: true);

            try
            {

                using var certificate  = X509Certificate2.CreateFromPem(certificatePEM);
                using var issuer       = X509Certificate2.CreateFromPem(issuerPEM);
                using var ofKey        = certificate.GetECDsaPublicKey();

                if (ofKey is null || !ofKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(accountKey.ExportSubjectPublicKeyInfo()))
                    return new ProviderResult(false, $"The e-mobility provider at {URL} certified another key than the one this phone asked for.", Upstream: true);

                if (!String.Equals(certificateId, Convert.ToHexString(SHA256.HashData(certificate.RawData)), StringComparison.OrdinalIgnoreCase))
                    return new ProviderResult(false, $"The e-mobility provider at {URL} names the account certificate otherwise than by its SHA-256.", Upstream: true);

            }
            catch (CryptographicException e)
            {
                return new ProviderResult(false, $"The e-mobility provider at {URL} sent a certificate that cannot be read: {e.Message}", Upstream: true);
            }

            var account = new ProviderAccount(URL.TrimEnd('/') + "/",
                                              Username,
                                              EMail,
                                              Password,
                                              party,
                                              issuerPEM,
                                              accountKey.ExportPkcs8PrivateKeyPem(),
                                              certificatePEM,
                                              certificateId.ToLowerInvariant(),
                                              TimeProvider.GetUtcNow(),
                                              MadeUp);

            if (!TryWriteOwnerOnly(ProviderAccountFile, account.ToStorageJSON(), out var error))
            {

                // Not kept, and so of no use: taken back, so that no key of this
                // phone is certified that nobody has.
                await Client.RevokeAccountKeyAsync(certificateId);

                return new ProviderResult(false, $"The account at {URL} could not be written down: {error}", NotSaved: true);

            }

            provider        = account;
            providerClient  = Client;

            Log.Notice($"'{Who}' {(MadeUp ? "made" : "took")} the account '{Username}' at the e-mobility provider {party} at {URL}; " +
                       $"this phone's account key is certified there ({certificateId[..16]}…), and every start asks for a charging ticket of its own.",
                       "provider", "web");

            return new ProviderResult(true, MadeUp
                                                ? $"This phone has the account '{Username}' at {party} now, with a password of its own, and an account key the provider certified."
                                                : $"This phone signs in at {party} as '{Username}' now, with an account key the provider certified.");

        }

        #endregion

        #region ForgetProviderAsync(Who)

        /// <summary>
        /// Forget the account at the e-mobility provider: its account key taken
        /// back there, where the provider can be reached, and its file deleted.
        /// The tickets the phone was issued stay, with the sessions they started.
        /// </summary>
        public async Task<ProviderResult> ForgetProviderAsync(String Who)
        {

            await providerLock.WaitAsync();

            try
            {

                if (provider is not ProviderAccount account)
                    return new ProviderResult(false, "This phone has no account at an e-mobility provider.");

                var revoked = await ClientOf(account).RevokeAccountKeyAsync(account.AccountCertificateId);

                try
                {
                    File.Delete(ProviderAccountFile);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    return new ProviderResult(false, $"The account could not be forgotten: '{ProviderAccountFile}' could not be deleted: {e.Message}", NotSaved: true);
                }

                provider        = null;
                providerClient  = null;

                Log.Notice($"'{Who}' had this phone forget its account '{account.Username}' at {account.Party} ({account.URL})" +
                           (revoked.Success ? "; its account key is taken back there." : $"; its account key could not be taken back there: {revoked.Problem}"),
                           "provider", "web");

                return new ProviderResult(true, revoked.Success
                                                    ? $"This phone has forgotten its account at {account.Party}, and its account key is taken back there."
                                                    : $"This phone has forgotten its account at {account.Party}. Its account key could not be taken back there: {revoked.Problem}");

            }
            finally
            {
                providerLock.Release();
            }

        }

        #endregion

        #region ProviderJSON()

        /// <summary>
        /// The account at the e-mobility provider, as its page shows it.
        /// </summary>
        public JObject ProviderJSON()
        {

            var account = provider;

            return new JObject(
                       new JProperty("set",             account is not null),
                       new JProperty("account",         account?.ToJSON()),
                       new JProperty("ticketValidity",  (Int32) ChargingTickets.Validity.TotalMinutes),
                       new JProperty("directory",       ProviderPath)
                   );

        }

        #endregion


        #region TicketForStartAsync(EVSEId, CancellationToken)

        /// <summary>
        /// A charging ticket for one start, from the e-mobility provider: a key
        /// made for it alone, the ticket good from now for an hour, signed by
        /// that key and the phone's account key - and back from the provider
        /// signed by its key and the provider's, the account's signature gone.
        /// Kept, with its key.
        /// </summary>
        public async Task<(IssuedTicket? Ticket, String? Error)> TicketForStartAsync(String             EVSEId,
                                                                                    CancellationToken  CancellationToken = default)
        {

            if (provider is not ProviderAccount account)
                return (null, "This phone has no account at an e-mobility provider.");

            using var ticketKey   = ChargingTickets.NewKey();
            using var accountKey  = account.AccountKey();
            using var issuer      = account.Issuer();

            var now       = DateTimeOffset.FromUnixTimeSeconds(TimeProvider.GetUtcNow().ToUnixTimeSeconds());
            var id        = RandomNumberGenerator.GetBytes(16);
            var payload   = ChargingTickets.Payload(id, account.Party, ticketKey, now, now + ChargingTickets.Validity);
            var kid       = Convert.FromHexString(account.AccountCertificateId);
            var request   = ChargingTickets.Request(payload, ticketKey, accountKey, kid);

            var answer    = await ClientOf(account).TicketAsync(request);

            if (!answer.Success)
                return (null, $"{account.Party} issued no charging ticket for {EVSEId}: {answer.Problem}");

            Byte[] issued;

            try
            {
                issued = Convert.FromBase64String(answer.JSON?["ticket"]?.Value<String>() ?? "");
            }
            catch (FormatException)
            {
                return (null, $"{account.Party} answered the ticket for {EVSEId} with no ticket.");
            }

            if (!ChargingTickets.TryCheckIssued(issued, payload, ticketKey, issuer, kid, out var problem))
                return (null, problem);

            var ticket = new IssuedTicket(Convert.ToHexStringLower(id),
                                          account.Party,
                                          now,
                                          now + ChargingTickets.Validity,
                                          issued,
                                          ticketKey.ExportPkcs8PrivateKeyPem(),
                                          ChargingTickets.Thumbprint(ticketKey),
                                          Convert.ToHexStringLower(SHA256.HashData(issuer.RawData)),
                                          EVSEId,
                                          TimeProvider.GetUtcNow());

            // Its key signs what is done with the ticket from now on: lost, and
            // the ticket is nobody's.
            Directory.CreateDirectory(ProviderTicketsPath);

            if (!TryWriteOwnerOnly(Path.Combine(ProviderTicketsPath, ticket.Id + ".json"), ticket.ToStorageJSON(), out var error))
                return (null, $"The charging ticket for {EVSEId} could not be kept, and with it its key: {error}");

            Log.Info($"{account.Party} issued the charging ticket {ticket.Id} for {EVSEId}, good until {ticket.NotAfter:o}: its key {ticket.KeyThumbprint[..16]}…, " +
                     "signed by the provider's ticket issuer and not by the account.",
                     "provider", "charging");

            return (ticket, null);

        }

        #endregion


        #region (private) ClientOf(Account)

        /// <summary>
        /// The client of the given account, with its session - made where there
        /// is none yet, e.g. after a start of the phone.
        /// </summary>
        private EMSPClient ClientOf(ProviderAccount Account)
        {

            var client = providerClient;

            if (client is null)
            {
                client = new EMSPClient(Account.URL, DNSClient, Account.Username, Account.Password);
                Interlocked.CompareExchange(ref providerClient, client, null);
                client = providerClient ?? client;
            }

            return client;

        }

        #endregion

        #region (private static) IsProviderURL(URL, out Problem)

        private static Boolean IsProviderURL(String URL, out String Problem)
        {

            Problem = "";

            if (String.IsNullOrWhiteSpace(URL) || !Uri.TryCreate(URL.Trim(), UriKind.Absolute, out var uri) ||
               (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) || String.IsNullOrEmpty(uri.Host))
            {
                Problem = $"'{URL}' is no http or https URL of an e-mobility provider.";
                return false;
            }

            return true;

        }

        #endregion

        #region (private) TryWriteOwnerOnly(File, JSON, out Error)

        /// <summary>
        /// Write a file nobody but the account the phone runs under may read:
        /// next to it first, then moved over it, so that a half-written one is
        /// never what is there.
        /// </summary>
        private Boolean TryWriteOwnerOnly(String File, JObject JSON, out String Error)
        {

            Error = "";

            var temporary = File + ".new";

            try
            {
                System.IO.File.WriteAllText(temporary, JSON.ToString(Formatting.Indented));
                OwnerOnly.File(temporary, problem => Log.Warning($"'{File}' could not be closed to everybody else: {problem}", "provider"));
                System.IO.File.Move(temporary, File, overwrite: true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Error = e.Message;
                try { System.IO.File.Delete(temporary); } catch (Exception) { }
                return false;
            }

        }

        #endregion

    }

}
