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

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.WWCP.Node;

using cloud.charging.open.Smartphone.Charging;
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

        /// <summary>
        /// How long after its ticket ran out the provider is still asked what
        /// became of a start: long enough for the operator's charge detail
        /// record, not for ever.
        /// </summary>
        public static readonly TimeSpan  ProviderReportWindow  = TimeSpan.FromDays(1);

        /// <summary>
        /// How long a page waits at most for what the provider says: an answer
        /// that takes longer is kept when it comes, and shown on the next ask.
        /// </summary>
        public static readonly TimeSpan  ProviderReportWait    = TimeSpan.FromSeconds(2);

        /// <summary>
        /// When the provider was last asked of each session, by its id.
        /// </summary>
        private readonly Dictionary<String, DateTimeOffset>  reportsAsked  = [];

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

        /// <summary>
        /// How often the provider is asked at most what became of one session
        /// started through it: every 15 seconds, while its pages ask.
        /// </summary>
        public TimeSpan          ProviderReportInterval    { get; set; } = TimeSpan.FromSeconds(15);

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
        /// Not while a session started through the provider charges.
        /// </summary>
        public async Task<ProviderResult> ForgetProviderAsync(String Who)
        {

            await providerLock.WaitAsync();

            try
            {

                if (provider is not ProviderAccount account)
                    return new ProviderResult(false, "This phone has no account at an e-mobility provider.");

                // A session started through the provider is stopped through it,
                // which takes the account.
                if (Sessions.Active(TimeProvider.GetUtcNow()).FirstOrDefault(session => session.Start is not null) is ChargingSession charging)
                    return new ProviderResult(false, $"This phone is charging through {account.Party} at {charging.EVSEId} ({charging.Id}): stop the session first, as only the account can.");

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

            if (!TryBase64(answer.JSON?["ticket"], out var issued))
                return (null, $"{account.Party} answered the ticket for {EVSEId} with no ticket.");

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

            if (!TryWriteOwnerOnly(TicketFile(ticket.Id), ticket.ToStorageJSON(), out var error))
                return (null, $"The charging ticket for {EVSEId} could not be kept, and with it its key: {error}");

            Log.Info($"{account.Party} issued the charging ticket {ticket.Id} for {EVSEId}, good until {ticket.NotAfter:o}: its key {ticket.KeyThumbprint[..16]}…, " +
                     "signed by the provider's ticket issuer and not by the account.",
                     "provider", "charging");

            return (ticket, null);

        }

        #endregion

        #region StartThroughProviderAsync(Ticket, EVSEId, EffectiveTree, TariffId, CancellationToken)

        /// <summary>
        /// Start charging through the e-mobility provider: a start request for
        /// this EVSE, now, signed by the ticket's key and sent with the ticket -
        /// and back countersigned by the provider's ticket issuer, with the
        /// token of this start alone and the authorization reference it sent the
        /// charge point operator. Kept with the ticket: it is what proves the
        /// driver asked for this start, and what the stop names.
        /// </summary>
        /// <param name="Ticket">The ticket of this start.</param>
        /// <param name="EVSEId">The EVSE, as the roaming network names it.</param>
        /// <param name="EffectiveTree">The digest of the version of the station's proof the phone checked, at a station of a roaming network.</param>
        /// <param name="TariffId">The tariff of that proof the price is of.</param>
        /// <param name="CancellationToken">To stop asking the provider.</param>
        public async Task<(ChargingStartReference? Start, String? Error)> StartThroughProviderAsync(IssuedTicket       Ticket,
                                                                                                   String             EVSEId,
                                                                                                   Byte[]?            EffectiveTree,
                                                                                                   String?            TariffId,
                                                                                                   CancellationToken  CancellationToken = default)
        {

            if (provider is not ProviderAccount account)
                return (null, "This phone has no account at an e-mobility provider.");

            using var ticketKey  = Ticket.TicketKey();
            using var issuer     = account.Issuer();

            var payload   = StartRequests.StartPayload(Ticket.Ticket, EVSEId, TimeProvider.GetUtcNow(), RandomNumberGenerator.GetBytes(16), EffectiveTree, TariffId);
            var request   = StartRequests.Sign(payload, StartRequests.StartContentType, ticketKey);
            var answer    = await ClientOf(account).StartAsync(Ticket.Id, Ticket.Ticket, request);

            if (!answer.Success)
                return (null, $"{account.Party} started nothing at {EVSEId}: {answer.Problem}" +
                              (answer.Status is 404 or 405 ? " Does it take start requests yet?" : ""));

            if (!TryBase64(answer.JSON?["request"], out var countersigned))
                return (null, $"{account.Party} answered the start at {EVSEId} with no start request.");

            if (!StartRequests.TryCheckCountersigned(countersigned, request, ticketKey, issuer, TimeProvider.GetUtcNow(), null, out var countersignature, out var problem))
                return (null, problem);

            var start = new ChargingStartReference(countersignature.Token,
                                                   countersignature.AuthorizationReference,
                                                   Convert.ToHexStringLower(StartRequests.Digest(countersigned)),
                                                   countersignature.SignedAt);

            if (!TryWriteOwnerOnly(TicketFile(Ticket.Id), (Ticket with { Start = countersigned }).ToStorageJSON(), out var error))
            {

                // Started, and not kept: no stop could name it, so it is stopped
                // now, while the phone still has it.
                var (_, _, stopProblem) = await StopAtProviderAsync(account, Ticket, countersigned, countersignature);

                return (null, $"{account.Party} started charging at {EVSEId}, and the start could not be kept with its ticket: {error} " +
                              (stopProblem is null ? "It was stopped again." : $"It could not be stopped again either: {stopProblem}"));

            }

            Log.Info($"{account.Party} countersigned the start at {EVSEId} with the charging ticket {Ticket.Id}: the token {start.Token}, the authorization reference {start.AuthorizationReference}.",
                     "provider", "charging");

            return (start, null);

        }

        #endregion

        #region StopThroughProviderAsync(Ticket, Start, CancellationToken)

        /// <summary>
        /// Stop a session that was started through the e-mobility provider: a
        /// stop request naming the ticket and the start, signed by the ticket's
        /// key, and back countersigned by the provider with the token and the
        /// reference of the start. Kept with the ticket.
        /// </summary>
        /// <param name="Ticket">The ticket the session was started with.</param>
        /// <param name="Start">What the session keeps of its start.</param>
        /// <param name="CancellationToken">To stop asking the provider.</param>
        public async Task<(ChargingStartReference? Stopped, String? Error)> StopThroughProviderAsync(ChargingTicketReference  Ticket,
                                                                                                    ChargingStartReference   Start,
                                                                                                    CancellationToken        CancellationToken = default)
        {

            if (provider is not ProviderAccount account)
                return (null, $"The session was started through {Ticket.Party}, and this phone has no account there any more to stop it with.");

            if (account.Party != Ticket.Party)
                return (null, $"The session was started through {Ticket.Party}, and this phone's account is at {account.Party}.");

            if (!TryReadTicket(Ticket.Id, out var issued, out var problem))
                return (null, problem);

            if (issued.Start is null || Convert.ToHexStringLower(StartRequests.Digest(issued.Start)) != Start.RequestDigest)
                return (null, $"The charging ticket {Ticket.Id} keeps no start, or another one than the session's: the session cannot be stopped with it.");

            var (stop, countersignature, stopProblem) = await StopAtProviderAsync(account,
                                                                                  issued,
                                                                                  issued.Start,
                                                                                  new ProviderCountersignature(Start.Token, Start.AuthorizationReference, Start.CountersignedAt));

            if (stop is null || countersignature is null)
                return (null, stopProblem);

            if (!TryWriteOwnerOnly(TicketFile(issued.Id), (issued with { Stop = stop }).ToStorageJSON(), out var error))
                Log.Warning($"{account.Party} countersigned the stop of {Start.Token}, and the stop could not be kept with the charging ticket {issued.Id}: {error}", "provider", "charging");

            return (Start with {
                        StopRequestDigest    = Convert.ToHexStringLower(StartRequests.Digest(stop)),
                        StopCountersignedAt  = countersignature.SignedAt
                    },
                    null);

        }

        #endregion

        #region ReportsFromProviderAsync(Sessions, CancellationToken)

        /// <summary>
        /// What the e-mobility provider says now of those of the given sessions
        /// that were started through it, kept with each: asked at most every
        /// <see cref="ProviderReportInterval"/> per session, and only while
        /// something of it is still to come - until the operator's charge
        /// detail record came or the start failed, and no longer than
        /// <see cref="ProviderReportWindow"/> after its ticket ran out. A
        /// provider that knows nothing of a start leaves it as it was. Waited
        /// for no longer than <see cref="ProviderReportWait"/>: a slow provider
        /// does not hold up the page, and what it says comes with the next.
        /// </summary>
        /// <param name="Sessions">The sessions a page is about to show.</param>
        /// <param name="CancellationToken">To stop waiting for the provider.</param>
        public async Task ReportsFromProviderAsync(IEnumerable<ChargingSession>  Sessions,
                                                   CancellationToken             CancellationToken = default)
        {

            if (provider is not ProviderAccount account)
                return;

            var now  = TimeProvider.GetUtcNow();
            var due  = Sessions.Where(session => session.Ticket is ChargingTicketReference ticket &&
                                                 session.Start  is not null                       &&
                                                 ticket.Party   == account.Party                  &&
                                                 now <= ticket.NotAfter + ProviderReportWindow    &&
                                                 session.Report?.IsFinal != true                  &&
                                                 IsReportDue(session.Id, now)).
                                ToArray();

            if (due.Length == 0)
                return;

            // Not cancelled with the page: an answer that comes late is still
            // kept, and nothing of it throws.
            var asking = Task.WhenAll(due.Select(session => ReportFromProviderAsync(account, session)));

            try
            {
                await Task.WhenAny(asking, Task.Delay(ProviderReportWait, CancellationToken));
            }
            catch (OperationCanceledException)
            {
                // The page went away: what the provider says is kept all the same.
            }

        }

        /// <summary>
        /// Whether the provider may be asked of the given session now - and if
        /// so, that it is being asked, so that nobody else asks at once.
        /// </summary>
        private Boolean IsReportDue(String Id, DateTimeOffset Now)
        {
            lock (reportsAsked)
            {

                if (reportsAsked.TryGetValue(Id, out var asked) && Now - asked < ProviderReportInterval)
                    return false;

                reportsAsked[Id] = Now;
                return true;

            }
        }

        /// <summary>
        /// What the provider says of one session, kept with it - and said in the
        /// log where it says something new.
        /// </summary>
        private async Task ReportFromProviderAsync(ProviderAccount  Account,
                                                   ChargingSession  Session)
        {

            var answer = await ClientOf(Account).StartsOfTicketAsync(Session.Ticket!.Id);

            // 404 or 405: a provider that knows nothing of the start, or does not
            // tell yet - the session stays as it is, simulated where it was.
            if (!answer.Success || answer.JSON is null)
                return;

            var report = ProviderReport.Of(answer.JSON, Session.Start!, TimeProvider.GetUtcNow(), out var problem);

            if (problem is not null)
                Log.Warning($"{Account.Party} answered what became of the session {Session.Id} ({Session.Start!.Token}) with what does not fit it: {problem}", "provider", "charging");

            if (report is null)
                return;

            if (!Sessions.TryReport(Session.Id, report, out _, out var error))
            {
                Log.Warning($"{Account.Party} reported of the session {Session.Id} that {report.InWords}, and this phone could not keep it: {error}", "provider", "charging");
                return;
            }

            if (Session.Report?.InWords != report.InWords)
                Log.Info($"{Account.Party} reports of the session {Session.Id} ({Session.Start!.Token}) at {Session.EVSEId}: {report.InWords}.", "provider", "charging");

        }

        #endregion

        #region (private) StopAtProviderAsync(Account, Ticket, Start, Of)

        /// <summary>
        /// The stop request of a start, signed by the ticket's key, sent with the
        /// ticket and the start, and checked as it comes back countersigned.
        /// </summary>
        private async Task<(Byte[]? Stop, ProviderCountersignature? Countersignature, String? Error)> StopAtProviderAsync(ProviderAccount           Account,
                                                                                                                         IssuedTicket              Ticket,
                                                                                                                         Byte[]                    Start,
                                                                                                                         ProviderCountersignature  Of)
        {

            using var ticketKey  = Ticket.TicketKey();
            using var issuer     = Account.Issuer();

            var payload  = StartRequests.StopPayload(Ticket.Ticket, Start, TimeProvider.GetUtcNow(), RandomNumberGenerator.GetBytes(16));
            var request  = StartRequests.Sign(payload, StartRequests.StopContentType, ticketKey);
            var answer   = await ClientOf(Account).StopAsync(Ticket.Id, Ticket.Ticket, Start, request);

            if (!answer.Success)
                return (null, null, $"{Account.Party} stopped nothing at {Ticket.EVSEId}: {answer.Problem}");

            if (!TryBase64(answer.JSON?["request"], out var countersigned))
                return (null, null, $"{Account.Party} answered the stop at {Ticket.EVSEId} with no stop request.");

            if (!StartRequests.TryCheckCountersigned(countersigned, request, ticketKey, issuer, TimeProvider.GetUtcNow(), Of, out var countersignature, out var problem))
                return (null, null, problem);

            Log.Info($"{Account.Party} countersigned the stop at {Ticket.EVSEId} of the token {Of.Token}.", "provider", "charging");

            return (countersigned, countersignature, null);

        }

        #endregion


        #region (private) TicketFile(Id) / TryReadTicket(Id, out Ticket, out Error)

        private String TicketFile(String Id)

            => Path.Combine(ProviderTicketsPath, Id + ".json");

        /// <summary>
        /// A ticket this phone was issued, as its file keeps it - by an id that
        /// is one, 16 bytes as hex, and so no path to anywhere else.
        /// </summary>
        private Boolean TryReadTicket(String                                  Id,
                                      [NotNullWhen(true)]  out IssuedTicket?  Ticket,
                                      [NotNullWhen(false)] out String?        Error)
        {

            Ticket = null;

            if (Id.Length != 32 || !Id.All(Uri.IsHexDigit))
            {
                Error = $"'{Id}' is no id of a charging ticket.";
                return false;
            }

            try
            {
                return IssuedTicket.TryParseStorage(JObject.Parse(File.ReadAllText(TicketFile(Id))), out Ticket, out Error);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
            {
                Error = $"The charging ticket {Id} could not be read, and with it its key: {e.Message}";
                return false;
            }

        }

        #endregion

        #region (private static) TryBase64(JSON, out Bytes)

        private static Boolean TryBase64(JToken? JSON, [NotNullWhen(true)] out Byte[]? Bytes)
        {

            Bytes = null;

            if (JSON?.Type != JTokenType.String)
                return false;

            try
            {
                Bytes = Convert.FromBase64String(JSON.Value<String>()!);
                return Bytes.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }

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
