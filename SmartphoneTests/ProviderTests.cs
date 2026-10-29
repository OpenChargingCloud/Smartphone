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

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.Mail;

using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.Smartphone.Charging;
using cloud.charging.open.Smartphone.Map;
using cloud.charging.open.Smartphone.Provider;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// The account of a smartphone at an e-mobility provider, against a
    /// provider on the loopback: made by an administrator - with a password the
    /// phone keeps, and an account key the provider certifies from a request
    /// made on the phone - and a charging ticket of its own for every start,
    /// which comes back without the account's signature in it, or is refused;
    /// a start and a stop request signed by the ticket's key, which come back
    /// countersigned by the provider, or are refused; signed in with a
    /// session, as the provider's web interface is, and anew where it is over;
    /// and the session as the provider reports it, once it does.
    /// </summary>
    public class ProviderTests
    {

        #region Data

        private const String  NoTimeServers  = """{ "nts": { "groups": { "legal": { "enabled": false } } } }""";

        private String       directory  = "";
        private Smartphone?  smartphone;
        private Uri?         address;
        private TestEMSP?    emsp;

        #endregion

        #region Setup / TearDown

        [SetUp]
        public async Task Setup()
        {

            directory  = Path.Combine(Path.GetTempPath(), "smartphone-provider-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(directory);

            emsp       = await TestEMSP.Start();

            await TestPorts.StartedOnFreshPorts(SmartphoneFrom);

        }

        [TearDown]
        public async Task TearDown()
        {

            if (smartphone is not null)
                await smartphone.DisposeAsync();

            if (emsp is not null)
                await emsp.DisposeAsync();

            // One instance runs every test of the fixture.
            smartphone  = null;
            emsp        = null;

            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // A temporary directory that outlives one test run is not worth
                // failing the run over.
            }

        }

        #endregion


        #region (helpers)

        private Smartphone SmartphoneFrom()
        {

            var port  = TestPorts.Free();
            var file  = Path.Combine(directory, WWCPConfigFile.DefaultFileName);

            File.WriteAllText(file, NoTimeServers);

            smartphone  = new Smartphone(
                              HTTPPort:          IPPort.Parse(port),
                              AccountsPath:      Path.Combine(directory, "accounts"),
                              ConfigFile:        new WWCPConfigFile(file),
                              CertificatesPath:  Path.Combine(directory, "certificates"),
                              LogToConsole:      false,
                              BridgeDebugLog:    false
                          );

            address     = new Uri($"http://127.0.0.1:{port}/");

            return smartphone;

        }

        private async Task<HttpClient> SignedInAs(String Name, String Role)
        {

            var password = "correct-horse-battery-" + Guid.NewGuid().ToString("N")[..8];

            Assert.That(smartphone!.ExtAPI.TryGetOrganization(Organization_Id.Parse("Smartphone"), out var organization) &&
                        organization is Organization, Is.True, "the smartphone's organization is not there");

            await smartphone.ExtAPI.CreateUser(User_Id.Parse(Name),
                                               I18NString.Create(Languages.en, Name),
                                               SimpleEMailAddress.Parse($"{Name}@localhost"),
                                               User2OrganizationEdgeLabel.IsMember,
                                               (Organization) organization!,
                                               Password:                  password,
                                               SkipDefaultNotifications:  true,
                                               SkipNewUserEMail:          true,
                                               SkipNewUserNotifications:  true,
                                               AcceptedEULA:              DateTimeOffset.UtcNow.AddSeconds(-1),
                                               IsAuthenticated:           true);

            Assert.That(smartphone.ExtAPI.TryGetUser(User_Id.Parse(Name), out var stored), Is.True);
            Assert.That(smartphone.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse(Role), out var group), Is.True, $"the smartphone has no group '{Role}'");
            Assert.That((await smartphone.ExtAPI.AddUserToUserGroup((User) stored!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!)).IsSuccess, Is.True);

            var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(60) };

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                                                             "Basic",
                                                             Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Name}:{password}"))
                                                         );

            return client;

        }

        private static async Task<(HttpStatusCode Status, JObject JSON)> Send(HttpClient Client, HttpMethod Method, String Path, Object? JSON = null)
        {

            using var request = new HttpRequestMessage(Method, Path);

            if (JSON is not null)
                request.Content = new StringContent(JObject.FromObject(JSON).ToString(), Encoding.UTF8, "application/json");

            var response = await Client.SendAsync(request);
            var text     = await response.Content.ReadAsStringAsync();

            return (response.StatusCode, text.Length > 0 ? JObject.Parse(text) : new JObject());

        }

        /// <summary>
        /// Demo points that are free now, and stay so for the next minute.
        /// </summary>
        private static (ChargingLocation Location, ChargingEVSE EVSE)[] FreePoints()
        {

            var now = DateTimeOffset.UtcNow;

            return [ .. DemoLocations.All.
                            SelectMany(location => location.EVSEs.Select(evse => (location, evse))).
                            Where(pair => DemoLocations.StatusOf(pair.evse, now)                         == EVSEStatus.Available &&
                                          DemoLocations.StatusOf(pair.evse, now + TimeSpan.FromMinutes(1)) == EVSEStatus.Available) ];

        }

        private String AccountFile
            => Path.Combine(directory, Smartphone.ProviderDirectoryName, "account.json");

        private JObject KeptTicket(String Id)
            => JObject.Parse(File.ReadAllText(Path.Combine(directory, Smartphone.ProviderDirectoryName, "tickets", Id + ".json")));

        private static CBORValue PayloadOf(Byte[] COSE)
            => CBORValue.Parse(COSESign.Parse(COSE).Payload!);

        private static CBORValue Of(CBORValue Map, String Key)
            => Map.TryGetValue(CBORValue.FromText(Key), out var value) ? value : throw new AssertionException($"'{Key}' is not in the payload.");

        private async Task<(HttpClient Admin, HttpClient Driver)> WithAnAccount()
        {

            var admin   = await SignedInAs("admin1",  "systemadmin");
            var driver  = await SignedInAs("driver1", "driver");

            var (status, signedUp) = await Send(admin, HttpMethod.Post, "api/v1/provider/signup", new { url = emsp!.URL, username = "phone1", email = "phone1@example.org" });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), signedUp.ToString());

            // Every page asks the provider anew, so that a test sees at once
            // what it had the operator report.
            smartphone!.ProviderReportInterval = TimeSpan.Zero;

            return (admin, driver);

        }

        #endregion


        #region AnAdministratorMakesTheAccountAndADriverOnlyLooks()

        /// <summary>
        /// An administrator makes the account: the provider gets a password the
        /// phone made up and a signing request of a key made on the phone, and
        /// certifies that key. The phone keeps password and key in a file of its
        /// own; a driver sees which account it is, and neither of them, and may
        /// make none.
        /// </summary>
        [Test]
        public async Task AnAdministratorMakesTheAccountAndADriverOnlyLooks()
        {

            using var admin   = await SignedInAs("admin1",  "systemadmin");
            using var driver  = await SignedInAs("driver1", "driver");

            var (status, refused) = await Send(driver, HttpMethod.Post, "api/v1/provider/signup", new { url = emsp!.URL, username = "phone1", email = "phone1@example.org" });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), refused.ToString());

            (status, var signedUp) = await Send(admin, HttpMethod.Post, "api/v1/provider/signup", new { url = emsp.URL, username = "phone1", email = "phone1@example.org" });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), signedUp.ToString());

            (status, var seen) = await Send(driver, HttpMethod.Get, "api/v1/provider");

            var kept     = JObject.Parse(File.ReadAllText(AccountFile));
            var keyId    = seen["account"]!["accountKey"]!["id"]!.Value<String>()!;
            var key      = emsp.AccountKeys[keyId.ToUpperInvariant()];

            using var ofKey  = key.Certificate.GetECDsaPublicKey()!;
            using var kept1  = ECDsa.Create();
            kept1.ImportFromPem(kept["accountKey"]!.Value<String>());

            using (Assert.EnterMultipleScope())
            {

                Assert.That(status,                                                Is.EqualTo(HttpStatusCode.OK), seen.ToString());
                Assert.That(seen["set"]!.Value<Boolean>(),                         Is.True);
                Assert.That(seen["account"]!["username"]!.Value<String>(),         Is.EqualTo("phone1"));
                Assert.That(seen["account"]!["party"]!.Value<String>(),            Is.EqualTo(TestEMSP.Party));
                Assert.That(seen["account"]!["madeUp"]!.Value<Boolean>(),          Is.True);
                Assert.That(seen.ToString(),                                       Does.Not.Contain(kept["password"]!.Value<String>()), "the password is shown");
                Assert.That(seen.ToString(),                                       Does.Not.Contain("PRIVATE KEY"),                    "the key is shown");

                Assert.That(key.Owner,                                             Is.EqualTo("phone1"));
                Assert.That(kept["password"]!.Value<String>()!.Length,             Is.GreaterThanOrEqualTo(32), "a password made up, and of a length nobody guesses");
                Assert.That(ofKey.ExportSubjectPublicKeyInfo(),                    Is.EqualTo(kept1.ExportSubjectPublicKeyInfo()), "the provider certified another key than the phone keeps");

            }

            (status, var twice) = await Send(admin, HttpMethod.Post, "api/v1/provider/signup", new { url = emsp.URL, username = "phone2", email = "phone2@example.org" });

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.BadRequest), twice.ToString());
            Assert.That(twice["error"]!.Value<String>(),    Does.Contain("forget it first"));

        }

        #endregion

        #region EveryStartHasATicketOfItsOwn()

        /// <summary>
        /// Every "Charge now!" asks for a ticket of its own: a key of its own,
        /// an id of its own, good for an hour - signed by that key and the
        /// account key, and back without the account's signature. The session
        /// names it; the ticket and its key are kept beside the account.
        /// </summary>
        [Test]
        public async Task EveryStartHasATicketOfItsOwn()
        {

            using var admin   = await SignedInAs("admin1",  "systemadmin");
            using var driver  = await SignedInAs("driver1", "driver");

            var (status, signedUp) = await Send(admin, HttpMethod.Post, "api/v1/provider/signup", new { url = emsp!.URL, username = "phone1", email = "phone1@example.org" });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), signedUp.ToString());

            var points = FreePoints();

            Assert.That(points, Has.Length.GreaterThanOrEqualTo(2), "two free demo points to charge at");

            (status, var first)  = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = points[0].Location.Id, evseUid = points[0].EVSE.Uid });
            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), first.ToString());

            (status, var second) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = points[1].Location.Id, evseUid = points[1].EVSE.Uid });
            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), second.ToString());

            var ticket1  = (JObject) first ["ticket"]!;
            var ticket2  = (JObject) second["ticket"]!;
            var issued   = emsp.Tickets;

            Assert.That(COSESign.TryParse(issued[0].Issued, out var cose, out var error), Is.True, error);

            var notBefore  = DateTimeOffset.Parse(ticket1["notBefore"]!.Value<String>()!, System.Globalization.CultureInfo.InvariantCulture);
            var notAfter   = DateTimeOffset.Parse(ticket1["notAfter"]!. Value<String>()!, System.Globalization.CultureInfo.InvariantCulture);
            var keptFile   = Path.Combine(directory, Smartphone.ProviderDirectoryName, "tickets", ticket1["id"]!.Value<String>() + ".json");

            using (Assert.EnterMultipleScope())
            {

                Assert.That(issued,                                                   Has.Count.EqualTo(2));
                Assert.That(issued.Select(ticket => ticket.Owner),                    Is.All.EqualTo("phone1"));
                Assert.That(ticket1["id"]!.Value<String>(),                           Is.Not.EqualTo(ticket2["id"]!.Value<String>()),            "two starts, one ticket");
                Assert.That(ticket1["keyThumbprint"]!.Value<String>(),                Is.Not.EqualTo(ticket2["keyThumbprint"]!.Value<String>()), "two starts, one key");
                Assert.That(ticket1["party"]!.Value<String>(),                        Is.EqualTo(TestEMSP.Party));
                Assert.That(notAfter - notBefore,                                     Is.EqualTo(TimeSpan.FromHours(1)));
                Assert.That(ticket1["issuerFingerprint"]!.Value<String>(),            Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(emsp.Issuer.RawData))));

                // What the provider sends back is what a charge point operator
                // sees: the ticket's key and the provider, and not the account.
                Assert.That(cose!.Signatures,                                         Has.Count.EqualTo(2));
                Assert.That(cose.Signatures.Select(signature => signature.ProtectedHeader.KeyIdentifier!.Length),
                                                                                      Is.EquivalentTo(new[] { 6, 32 }));

                Assert.That(File.Exists(keptFile),                                    Is.True, "the ticket and its key are not kept");
                Assert.That(JObject.Parse(File.ReadAllText(keptFile))["ticketKey"]!.Value<String>(), Does.Contain("PRIVATE KEY"));

            }

            (status, var kept) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{first["id"]}");

            Assert.That(kept["ticket"]!["id"]!.Value<String>(), Is.EqualTo(ticket1["id"]!.Value<String>()), "the session forgot its ticket");

            // The session of the sign-up served both starts; one that is over
            // is taken anew, once.
            Assert.That(emsp.SignIns, Is.EqualTo(1), "signed in again for every start");

            emsp.EndSessions();

            var (third, problem) = await smartphone!.TicketForStartAsync("DE*GEF*E1*1");

            Assert.That(third,        Is.Not.Null, problem);
            Assert.That(emsp.SignIns, Is.EqualTo(2));

        }

        #endregion

        #region ATicketThatNamesTheAccountIsRefused(How)

        /// <summary>
        /// A provider that sends a ticket back with the account's signature
        /// still in it - which would tell the charge point operator whose it is
        /// - or with another payload than was asked for: the phone refuses it,
        /// and starts nothing.
        /// </summary>
        [TestCase("keepAccountSignature", "account's signature still in it")]
        [TestCase("changePayload",        "its payload was changed")]
        public async Task ATicketThatNamesTheAccountIsRefused(String How, String Said)
        {

            using var admin   = await SignedInAs("admin1",  "systemadmin");
            using var driver  = await SignedInAs("driver1", "driver");

            var (status, signedUp) = await Send(admin, HttpMethod.Post, "api/v1/provider/signup", new { url = emsp!.URL, username = "phone1", email = "phone1@example.org" });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), signedUp.ToString());

            emsp.Misbehave = How;

            var point = FreePoints().First();

            (status, var refused) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            (_, var sessions) = await Send(driver, HttpMethod.Get, "api/v1/charging/sessions");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                               Is.EqualTo(HttpStatusCode.BadGateway), refused.ToString());
                Assert.That(refused["error"]!.Value<String>(),    Does.Contain(Said));
                Assert.That(sessions["total"]!.Value<Int32>(),    Is.Zero, "a session was started with a ticket that was refused");
            }

        }

        #endregion

        #region AStartAndAStopGoThroughTheProvider()

        /// <summary>
        /// "Charge now!" with an account: a start request for exactly this EVSE,
        /// now, naming the ticket, signed by the ticket's key alone - and back
        /// countersigned by the provider's ticket issuer, with a token of this
        /// start alone. "Stop" names that start, and comes back countersigned
        /// with the same token. Both are kept with the ticket; the session says
        /// what the provider said. The account is not forgotten while it charges.
        /// </summary>
        [Test]
        public async Task AStartAndAStopGoThroughTheProvider()
        {

            var (admin, driver) = await WithAnAccount();

            using var _admin   = admin;
            using var _driver  = driver;

            var point = FreePoints().First();

            var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            var ticket   = emsp!.Tickets.Single().Issued;
            var start    = emsp.Starts.Single();
            var payload  = PayloadOf(start.Request);
            var signed   = COSESign.Parse(start.Countersigned);
            var kept     = KeptTicket(session["ticket"]!["id"]!.Value<String>()!);

            using (Assert.EnterMultipleScope())
            {

                Assert.That(Of(payload, "typ").AsText(),                                Is.EqualTo(StartRequests.StartType));
                Assert.That(Of(payload, "evse").AsText(),                               Is.EqualTo(point.EVSE.EVSEId));
                Assert.That(Of(payload, "ticket").AsBytes(),                            Is.EqualTo(SHA256.HashData(ticket)), "the start names another ticket");
                Assert.That(Of(payload, "nonce").AsBytes(),                             Has.Length.EqualTo(16));
                Assert.That(payload.TryGetValue(CBORValue.FromText("proof"), out _),    Is.False, "a demo station has no proof to name");
                Assert.That(COSESign.Parse(start.Request).Signatures,                   Has.Count.EqualTo(1), "the phone signs alone");
                Assert.That(signed.Signatures,                                          Has.Count.EqualTo(2));

                Assert.That(session["start"]!["token"]!.Value<String>(),                   Is.EqualTo(start.Token));
                Assert.That(session["start"]!["authorizationReference"]!.Value<String>(),  Is.EqualTo(start.AuthRef));
                Assert.That(session["start"]!["requestDigest"]!.Value<String>(),           Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(start.Countersigned))));
                Assert.That(kept["start"]!.Value<String>(),                                Is.EqualTo(Convert.ToBase64String(start.Countersigned)), "the start is not kept with its ticket");

            }

            (status, var forgotten) = await Send(admin, HttpMethod.Post, "api/v1/provider/forget", new { });

            Assert.That(status,                                 Is.EqualTo(HttpStatusCode.BadRequest), forgotten.ToString());
            Assert.That(forgotten["error"]!.Value<String>(),    Does.Contain("stop the session first"));

            (status, var stopped) = await Send(driver, HttpMethod.Post, $"api/v1/charging/sessions/{session["id"]}/stop");

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), stopped.ToString());

            var stop       = emsp.Stops.Single();
            var ofStop     = PayloadOf(stop.Request);

            kept = KeptTicket(session["ticket"]!["id"]!.Value<String>()!);

            using (Assert.EnterMultipleScope())
            {

                Assert.That(stopped["status"]!.Value<String>(),                           Is.EqualTo("stopped"));
                Assert.That(Of(ofStop, "typ").AsText(),                                   Is.EqualTo(StartRequests.StopType));
                Assert.That(Of(ofStop, "start").AsBytes(),                                Is.EqualTo(SHA256.HashData(start.Countersigned)), "the stop names another start");
                Assert.That(Of(ofStop, "ticket").AsBytes(),                               Is.EqualTo(SHA256.HashData(ticket)));
                Assert.That(stop.Token,                                                   Is.EqualTo(start.Token));
                Assert.That(stopped["start"]!["stopRequestDigest"]!.Value<String>(),      Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(stop.Countersigned))));
                Assert.That(kept["stop"]!.Value<String>(),                                Is.EqualTo(Convert.ToBase64String(stop.Countersigned)), "the stop is not kept with its ticket");

            }

            // What the session says of its start outlives a restart.
            await smartphone!.DisposeAsync();
            smartphone = null;

            await TestPorts.StartedOnFreshPorts(SmartphoneFrom);

            using var again = await SignedInAs("driver2", "driver");

            (status, var read) = await Send(again, HttpMethod.Get, $"api/v1/charging/sessions/{session["id"]}");

            Assert.That(read["start"]!["stopRequestDigest"]!.Value<String>(), Is.EqualTo(stopped["start"]!["stopRequestDigest"]!.Value<String>()), read.ToString());

        }

        #endregion

        #region ACountersignatureThatDoesNotHoldStartsNothing(How, Said)

        /// <summary>
        /// A provider that takes no start requests, refuses the start, or sends
        /// it back countersigned in a way that does not hold - its token where
        /// its signature does not cover it, by another key than its ticket
        /// issuer's, an hour off, or with the phone's own signature replaced by
        /// another: the phone says so, and starts nothing.
        /// </summary>
        [TestCase("takesNoStarts",            "Does it take start requests yet?")]
        [TestCase("refuseStart",              "peered with no charge point operator")]
        [TestCase("countersignUnprotected",   "in its protected header")]
        [TestCase("countersignWithOtherKey",  "not countersigned by its ticket issuer")]
        [TestCase("countersignAnHourOff",     "off this phone's clock")]
        [TestCase("replaceOwnSignature",      "without this phone's own signature")]
        public async Task ACountersignatureThatDoesNotHoldStartsNothing(String How, String Said)
        {

            var (admin, driver) = await WithAnAccount();

            using var _admin   = admin;
            using var _driver  = driver;

            emsp!.Misbehave = How;

            var point = FreePoints().First();

            var (status, refused) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            (_, var sessions) = await Send(driver, HttpMethod.Get, "api/v1/charging/sessions");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                               Is.EqualTo(HttpStatusCode.BadGateway), refused.ToString());
                Assert.That(refused["error"]!.Value<String>(),    Does.Contain(Said));
                Assert.That(sessions["total"]!.Value<Int32>(),    Is.Zero, "a session was started with a start that was refused");
            }

        }

        #endregion

        #region AStopThatDoesNotHoldLeavesTheSessionCharging()

        /// <summary>
        /// A stop the provider countersigns with another token than its start's
        /// is refused: the session goes on charging, as the operator reports it,
        /// and is stopped by the next stop that holds.
        /// </summary>
        [Test]
        public async Task AStopThatDoesNotHoldLeavesTheSessionCharging()
        {

            var (admin, driver) = await WithAnAccount();

            using var _admin   = admin;
            using var _driver  = driver;

            var point = FreePoints().First();

            var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            emsp!.OperatorSession(emsp.Starts.Single().Token, "ACTIVE", 1.5m);
            emsp.Misbehave = "otherTokenOnStop";

            (status, var refused) = await Send(driver, HttpMethod.Post, $"api/v1/charging/sessions/{session["id"]}/stop");
            (_,      var still)   = await Send(driver, HttpMethod.Get,  $"api/v1/charging/sessions/{session["id"]}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                               Is.EqualTo(HttpStatusCode.BadGateway), refused.ToString());
                Assert.That(refused["error"]!.Value<String>(),    Does.Contain("another token"));
                Assert.That(still["status"]!.Value<String>(),     Is.EqualTo("charging"));
            }

            emsp.Misbehave = null;

            (status, var stopped) = await Send(driver, HttpMethod.Post, $"api/v1/charging/sessions/{session["id"]}/stop");

            Assert.That(status,                               Is.EqualTo(HttpStatusCode.OK), stopped.ToString());
            Assert.That(stopped["status"]!.Value<String>(),   Is.EqualTo("stopped"));

        }

        #endregion

        #region WhatTheProviderReportsIsWhatThePhoneShows()

        /// <summary>
        /// From the provider's first report on, the session is what the charge
        /// point operator told it, and nothing simulated: starting while it
        /// reported no session, charging with the session's kWh and cost, and
        /// finished with the figures of its charge detail record - whose signed
        /// meter values are kept as they came. So it is after a restart, and the
        /// provider is not asked again of a session whose record came.
        /// </summary>
        [Test]
        public async Task WhatTheProviderReportsIsWhatThePhoneShows()
        {

            var (admin, driver) = await WithAnAccount();

            using var _admin   = admin;
            using var _driver  = driver;

            var point = FreePoints().First();

            var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            var id     = session["id"]!.Value<String>()!;
            var token  = emsp!.Starts.Single().Token;

            (_, var starting) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            emsp.OperatorResult (token, "ACCEPTED");
            emsp.OperatorSession(token, "ACTIVE", 4.2m, 2.73m);

            (_, var charging) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{id}");
            (_, var active)   = await Send(driver, HttpMethod.Get, "api/v1/charging/sessions?active=true");

            var end = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            emsp.OperatorSession(token, "COMPLETED", 12.5m, 8.13m);
            emsp.OperatorCDR    (token, 12.5m, 8.13m, end);

            (_, var finished) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            var kept = JArray.Parse(File.ReadAllText(Path.Combine(directory, ChargingSessions.DefaultFileName))).Single()["report"]!;

            using (Assert.EnterMultipleScope())
            {

                Assert.That(session ["simulated"]!.Value<Boolean>(),                   Is.True,  "the provider was asked nothing yet");

                Assert.That(starting["status"]!.Value<String>(),                       Is.EqualTo("starting"), starting.ToString());
                Assert.That(starting["simulated"]!.Value<Boolean>(),                   Is.False);
                Assert.That(starting["energyKWh"]!.Value<Decimal>(),                   Is.Zero);
                Assert.That(starting["provider"]!["state"]!.Value<String>(),           Is.EqualTo("started"));
                Assert.That(starting["powerKW"],                                       Is.Null, "OCPI tells no power, and none is simulated");

                Assert.That(charging["status"]!.Value<String>(),                       Is.EqualTo("charging"), charging.ToString());
                Assert.That(charging["energyKWh"]!.Value<Decimal>(),                   Is.EqualTo(4.2m));
                Assert.That(charging["cost"]!["amount"]!.Value<Decimal>(),             Is.EqualTo(2.73m));
                Assert.That(charging["cost"]!["reported"]!.Value<Boolean>(),           Is.True);
                Assert.That(charging["provider"]!["result"]!.Value<String>(),          Is.EqualTo("ACCEPTED"));
                Assert.That(charging["provider"]!["sessionStatus"]!.Value<String>(),   Is.EqualTo("ACTIVE"));
                Assert.That(charging["chargingSince"],                                 Is.Not.Null);
                Assert.That(active  ["active"]!.Value<Int32>(),                        Is.EqualTo(1));
                Assert.That(active  ["simulated"]!.Value<Boolean>(),                   Is.False);

                Assert.That(finished["status"]!.Value<String>(),                       Is.EqualTo("finished"), finished.ToString());
                Assert.That(finished["energyKWh"]!.Value<Decimal>(),                   Is.EqualTo(12.5m));
                Assert.That(finished["cost"]!["amount"]!.Value<Decimal>(),             Is.EqualTo(8.13m));
                Assert.That(finished["provider"]!["cdr"]!.Value<Boolean>(),            Is.True);
                Assert.That(new DateTimeOffset(finished["endedAt"]!.Value<DateTime>()), Is.EqualTo(end), "it ended when the charge detail record says");

                Assert.That(kept["cdr"]!["signed_data"]!["signed_values"]![0]!["plain_data"]!.Value<String>(), Is.EqualTo(TestEMSP.SignedMeterValue), "the signed meter values are not kept as they came");

            }

            var reads = emsp.TicketReads;

            await smartphone!.DisposeAsync();
            smartphone = null;

            await TestPorts.StartedOnFreshPorts(SmartphoneFrom);

            smartphone!.ProviderReportInterval = TimeSpan.Zero;

            using var again = await SignedInAs("driver2", "driver");

            (_, var read) = await Send(again, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(read["status"]!.Value<String>(),     Is.EqualTo("finished"), read.ToString());
                Assert.That(read["energyKWh"]!.Value<Decimal>(), Is.EqualTo(12.5m));
                Assert.That(emsp.TicketReads,                    Is.EqualTo(reads), "the provider was asked again of a session whose record came");
            }

        }

        #endregion

        #region AStartTheOperatorDidNotTakeFailsAndFreesTheEVSE()

        /// <summary>
        /// A start the operator answers with another CommandResult than ACCEPTED
        /// failed: the session says so and why, cannot be stopped, and the EVSE
        /// is free for the next start.
        /// </summary>
        [Test]
        public async Task AStartTheOperatorDidNotTakeFailsAndFreesTheEVSE()
        {

            var (admin, driver) = await WithAnAccount();

            using var _admin   = admin;
            using var _driver  = driver;

            var point = FreePoints().First();

            var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            emsp!.OperatorResult(emsp.Starts.Single().Token, "EVSE_OCCUPIED", After: TimeSpan.FromMinutes(1));

            (_,      var failed)      = await Send(driver, HttpMethod.Get,  $"api/v1/charging/sessions/{session["id"]}");
            (status, var notStopped)  = await Send(driver, HttpMethod.Post, $"api/v1/charging/sessions/{session["id"]}/stop");

            var stopStatus = status;

            (status, var next) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(failed["status"]!.Value<String>(),               Is.EqualTo("failed"), failed.ToString());
                Assert.That(failed["provider"]!["state"]!.Value<String>(),   Is.EqualTo("failed"));
                Assert.That(failed["provider"]!["result"]!.Value<String>(),  Is.EqualTo("EVSE_OCCUPIED"));
                Assert.That(failed["endedAt"],                               Is.Not.Null);
                Assert.That(failed["durationSeconds"]!.Value<Int64>(),       Is.Zero, "a start that failed charged for a while");
                Assert.That(stopStatus,                                      Is.EqualTo(HttpStatusCode.Conflict), notStopped.ToString());
                Assert.That(emsp.Stops,                                      Is.Empty, "a failed start was stopped at the provider");
                Assert.That(status,                                          Is.EqualTo(HttpStatusCode.Created), next.ToString());
                Assert.That(emsp.Starts,                                     Has.Count.EqualTo(2));
            }

        }

        #endregion

        #region WhatIsNotOfTheStartIsNotTaken(How, Simulated, Shown)

        /// <summary>
        /// A provider that tells nothing of the start leaves its session
        /// simulated, as before; one that tells of it with another token is not
        /// believed at all; and a session the operator pushed for another start
        /// is left out, the start then as far as the provider got without it.
        /// </summary>
        [TestCase("reportsNothing",      true,   "charging")]
        [TestCase("reportOtherToken",    true,   "charging")]
        [TestCase("reportOtherAuthRef",  false,  "starting")]
        public async Task WhatIsNotOfTheStartIsNotTaken(String How, Boolean Simulated, String Shown)
        {

            var (admin, driver) = await WithAnAccount();

            using var _admin   = admin;
            using var _driver  = driver;

            var point = FreePoints().First();

            var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            emsp!.OperatorSession(emsp.Starts.Single().Token, "ACTIVE", 4.2m, 2.73m);
            emsp.Misbehave = How;

            (_, var read) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{session["id"]}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(read["simulated"]!.Value<Boolean>(),  Is.EqualTo(Simulated), read.ToString());
                Assert.That(read["status"]!.Value<String>(),      Is.EqualTo(Shown));
                Assert.That(read["energyKWh"]!.Value<Decimal>(),  Is.Not.EqualTo(4.2m), "what is not of the start was taken");
                Assert.That(emsp.TicketReads,                     Is.EqualTo(1));
            }

        }

        #endregion

        #region ASlowProviderDoesNotHoldUpThePage()

        /// <summary>
        /// A provider that takes longer to answer than a page waits does not
        /// hold the page up: it is shown as it was, and what the provider says
        /// is kept when it comes, and shown on the next ask.
        /// </summary>
        [Test]
        public async Task ASlowProviderDoesNotHoldUpThePage()
        {

            var (admin, driver) = await WithAnAccount();

            using var _admin   = admin;
            using var _driver  = driver;

            // Asked once: what comes late is kept, and not asked for again.
            smartphone!.ProviderReportInterval = TimeSpan.FromSeconds(30);

            var point = FreePoints().First();

            var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            emsp!.OperatorSession(emsp.Starts.Single().Token, "ACTIVE", 4.2m, 2.73m);
            emsp.Misbehave = "reportsSlowly";

            var watch = System.Diagnostics.Stopwatch.StartNew();

            (_, var first) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{session["id"]}");

            var waited = watch.Elapsed;

            JObject later;

            do
            {
                await Task.Delay(250);
                (_, later) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{session["id"]}");
            }
            while (later["simulated"]!.Value<Boolean>() && watch.Elapsed < TimeSpan.FromSeconds(15));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(waited,                                Is.LessThan(TimeSpan.FromSeconds(3.5)), "the page waited for the provider");
                Assert.That(first["simulated"]!.Value<Boolean>(),  Is.True, first.ToString());
                Assert.That(later["status"]!.Value<String>(),      Is.EqualTo("charging"), later.ToString());
                Assert.That(later["energyKWh"]!.Value<Decimal>(),  Is.EqualTo(4.2m), "what came late was not kept");
                Assert.That(emsp.TicketReads,                      Is.EqualTo(1));
            }

        }

        #endregion

        #region TheProviderIsAskedAtMostEveryFifteenSeconds()

        /// <summary>
        /// However often the pages ask, the provider is asked of one session at
        /// most every 15 seconds.
        /// </summary>
        [Test]
        public async Task TheProviderIsAskedAtMostEveryFifteenSeconds()
        {

            var (admin, driver) = await WithAnAccount();

            using var _admin   = admin;
            using var _driver  = driver;

            smartphone!.ProviderReportInterval = TimeSpan.FromSeconds(15);

            var point = FreePoints().First();

            var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{session["id"]}");
            await Send(driver, HttpMethod.Get, "api/v1/charging/sessions");
            await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{session["id"]}");

            Assert.That(emsp!.TicketReads, Is.EqualTo(1));

        }

        #endregion

        #region AnAccountOfTheDriversIsTakenAndForgotten()

        /// <summary>
        /// An account the driver made in a browser is taken with its password -
        /// a wrong one is told so - and forgotten again: its account key taken
        /// back at the provider, its file gone, and a start without a ticket.
        /// </summary>
        [Test]
        public async Task AnAccountOfTheDriversIsTakenAndForgotten()
        {

            emsp!.SignedUp("alice", "correct horse battery staple");

            using var admin = await SignedInAs("admin1", "systemadmin");

            var (status, wrong) = await Send(admin, HttpMethod.Post, "api/v1/provider/signin", new { url = emsp.URL, username = "alice", password = "Tr0ub4dor&3" });

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.BadRequest), wrong.ToString());
            Assert.That(wrong["error"]!.Value<String>(),    Does.Contain("did not let 'alice' in"));

            (status, var signedIn) = await Send(admin, HttpMethod.Post, "api/v1/provider/signin", new { url = emsp.URL, username = "alice", password = "correct horse battery staple" });

            Assert.That(status,                                          Is.EqualTo(HttpStatusCode.OK), signedIn.ToString());
            Assert.That(signedIn["account"]!["madeUp"]!.Value<Boolean>(), Is.False);

            var keyId = signedIn["account"]!["accountKey"]!["id"]!.Value<String>()!.ToUpperInvariant();

            (status, var forgotten) = await Send(admin, HttpMethod.Post, "api/v1/provider/forget", new { });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), forgotten.ToString());

            var point = FreePoints().First();

            (status, var session) = await Send(admin, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = point.Location.Id, evseUid = point.EVSE.Uid });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(forgotten["set"]!.Value<Boolean>(),   Is.False);
                Assert.That(emsp.AccountKeys[keyId].Revoked,      Is.True,  "the account key was not taken back at the provider");
                Assert.That(File.Exists(AccountFile),             Is.False, "the account is still kept");
                Assert.That(status,                               Is.EqualTo(HttpStatusCode.Created), session.ToString());
                Assert.That(session["ticket"],                    Is.Null, "a ticket without an account");
                Assert.That(emsp.Tickets,                         Is.Empty);
            }

        }

        #endregion

        #region TheAccountIsThereAfterARestart()

        /// <summary>
        /// The account and its key are read back at the next start, and the
        /// next "Charge now!" gets its ticket with them.
        /// </summary>
        [Test]
        public async Task TheAccountIsThereAfterARestart()
        {

            using (var admin = await SignedInAs("admin1", "systemadmin"))
            {
                var (status, signedUp) = await Send(admin, HttpMethod.Post, "api/v1/provider/signup", new { url = emsp!.URL, username = "phone1", email = "phone1@example.org" });
                Assert.That(status, Is.EqualTo(HttpStatusCode.OK), signedUp.ToString());
            }

            await smartphone!.DisposeAsync();
            smartphone = null;

            await TestPorts.StartedOnFreshPorts(SmartphoneFrom);

            Assert.That(smartphone!.ProviderAccount?.Username, Is.EqualTo("phone1"));

            var (ticket, error) = await smartphone.TicketForStartAsync("DE*GEF*E1*1");

            Assert.That(ticket,         Is.Not.Null, error);
            Assert.That(emsp!.Tickets,  Has.Count.EqualTo(1));
            Assert.That(emsp.SignIns,   Is.EqualTo(2), "signed up, and signed in with the password it kept");

        }

        #endregion

    }

}
