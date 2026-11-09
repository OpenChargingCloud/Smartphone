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
using System.Text;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;
using org.GraphDefined.Vanaheimr.Hermod.Mail;

using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;
using cloud.charging.open.protocols.WWCP.POI;

using cloud.charging.open.Smartphone.Charging;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// The map of a smartphone given the trust anchor of a roaming network,
    /// against a roaming network served on the loopback: its charging pools on
    /// the map, the proof of each station checked against the identity keys of
    /// the trust anchor an administrator trusts - and refused where nobody it
    /// trusts attested it - and everything a proof that holds gives, its
    /// tariffs, its price statements and the public keys its energy meters
    /// sign their measurements with.
    /// </summary>
    public class RoamingNetworkTests
    {

        #region Data

        private const String  NoTimeServers  = """{ "nts": { "groups": { "legal": { "enabled": false } } } }""";

        private String               directory  = "";
        private Smartphone?          smartphone;
        private Uri?                 address;
        private TestRoamingNetwork?  network;
        private TestRoamingNetwork?  other;
        private TestEMSP?            emsp;
        private TestCPO?             cpo;

        #endregion

        #region Setup / TearDown

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "smartphone-poi-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public async Task TearDown()
        {

            if (smartphone is not null)
                await smartphone.DisposeAsync();

            if (network is not null)
                await network.DisposeAsync();

            if (other is not null)
                await other.DisposeAsync();

            if (emsp is not null)
                await emsp.DisposeAsync();

            if (cpo is not null)
                await cpo.DisposeAsync();

            // One instance runs every test of the fixture: a network of the
            // test before, disposed, is none of the next.
            smartphone  = null;
            network     = null;
            other       = null;
            emsp        = null;
            cpo         = null;

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

        /// <summary>
        /// A smartphone whose time client is switched off, on a free port of
        /// the loopback - made, and not yet started - its configuration file
        /// the given one, or one saying no more than that.
        /// </summary>
        private Smartphone SmartphoneFrom(String? Configuration = null)
        {

            var port  = TestPorts.Free();
            var file  = Path.Combine(directory, WWCPConfigFile.DefaultFileName);

            File.WriteAllText(file, Configuration ?? NoTimeServers);

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

        /// <summary>
        /// A client signed in with a password as an account made for the
        /// purpose, in the group of the given role.
        /// </summary>
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
        /// The session once it is as the given condition says, asked for every
        /// tenth of a second - for ten seconds at most.
        /// </summary>
        private static async Task<JObject> SessionWhen(HttpClient Client, String Id, Func<JObject, Boolean> Condition)
        {

            var until = DateTimeOffset.UtcNow.AddSeconds(10);

            while (true)
            {

                var (_, session) = await Send(Client, HttpMethod.Get, $"api/v1/charging/sessions/{Id}");

                if (Condition(session))
                    return session;

                if (DateTimeOffset.UtcNow > until)
                    Assert.Fail($"The session never came to be as expected: {session}");

                await Task.Delay(100);

            }

        }

        /// <summary>
        /// A started smartphone, the roaming network of the tests, and an
        /// administrator who gave the smartphone its trust anchor - as the text
        /// of its QR code - and trusts every identity key it names.
        /// </summary>
        private async Task<HttpClient> WithTheRoamingNetwork(RoamingNetworkTrustAnchor? TrustAnchor = null)
        {

            network ??= await TestRoamingNetwork.Start();

            await TestPorts.StartedOnFreshPorts(() => SmartphoneFrom());

            var admin   = await SignedInAs("admin1", "systemadmin");
            var anchor  = TrustAnchor ?? network.TrustAnchor;

            var (status, settings) = await Send(admin, HttpMethod.Put, "api/v1/configuration/roamingNetwork",
                                                new { trustAnchor = anchor.ToQRCode(), trustedKeys = anchor.Keys.ToArray() });

            Assert.That(status,                                       Is.EqualTo(HttpStatusCode.OK), settings.ToString());
            Assert.That(settings["quorumTrusted"]!.Value<Boolean>(),  Is.True,                       settings.ToString());

            return admin;

        }

        /// <summary>
        /// A session started through the stand-in e-mobility provider at the
        /// DC EVSE of the roaming network, its station checked before: the
        /// administrator, the driver, the session and the token of its start.
        /// </summary>
        private async Task<(HttpClient Admin, HttpClient Driver, JObject Session, String Token)> ChargingThroughTheProvider()
        {

            emsp = await TestEMSP.Start();

            var admin   = await WithTheRoamingNetwork();
            var driver  = await SignedInAs("driver1", "driver");

            var (status, signedUp) = await Send(admin, HttpMethod.Post, "api/v1/provider/signup", new { url = emsp.URL, username = "phone1", email = "phone1@example.org" });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), signedUp.ToString());

            (status, var check) = await Send(driver, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            Assert.That(check["verified"]!.Value<Boolean>(), Is.True, check.ToString());

            (status, var session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.DCEVSE });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            return (admin, driver, session, session["start"]!["token"]!.Value<String>()!);

        }

        /// <summary>
        /// Signed values of a charge detail record, as OCPI writes them: each
        /// an OCMF document as its signed data.
        /// </summary>
        private static JArray SignedValues(params (String Nature, String OCMF)[] Values)

            => new (Values.Select(value => new JObject(
                        new JProperty("nature",       value.Nature),
                        new JProperty("plain_data",   value.Nature),
                        new JProperty("signed_data",  value.OCMF)
                    )));

        private static DateTimeOffset StartedAt(JObject Session)

            => new (Session["startedAt"]!.Value<DateTime>().ToUniversalTime(), TimeSpan.Zero);

        #endregion


        #region TheTrustAnchorIsTheAdministratorsToSet()

        /// <summary>
        /// The trust anchor is none at first, set by an administrator - as the
        /// text of its QR code, its URI or its JSON, kept as its JSON in the
        /// configuration file - and refused to a driver and where it is none.
        /// </summary>
        [Test]
        public async Task TheTrustAnchorIsTheAdministratorsToSet()
        {

            network = await TestRoamingNetwork.Start();

            await TestPorts.StartedOnFreshPorts(() => SmartphoneFrom());

            using var admin   = await SignedInAs("admin1",  "systemadmin");
            using var driver  = await SignedInAs("driver1", "driver");

            var (status, settings) = await Send(driver, HttpMethod.Get, "api/v1/configuration/roamingNetwork");

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.OK), settings.ToString());
            Assert.That(settings["set"]!.Value<Boolean>(),  Is.False);

            (status, var refused) = await Send(driver, HttpMethod.Put, "api/v1/configuration/roamingNetwork", new { trustAnchor = network.TrustAnchor.ToQRCode() });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), refused.ToString());

            (status, var wrong) = await Send(admin, HttpMethod.Put, "api/v1/configuration/roamingNetwork", new { trustAnchor = "WWCP/TA-2:NOT BASE45 AT ALL" });

            Assert.That(status,                           Is.EqualTo(HttpStatusCode.BadRequest), wrong.ToString());
            Assert.That(wrong["error"]!.Value<String>(),  Does.StartWith("That is no trust anchor"));

            foreach (var given in new Object[] { network.TrustAnchor.ToQRCode(), network.TrustAnchor.ToURL(), network.TrustAnchor.ToJSON().ToString() })
            {

                (status, settings) = await Send(admin, HttpMethod.Put, "api/v1/configuration/roamingNetwork", new { trustAnchor = given });

                Assert.That(status,                                                              Is.EqualTo(HttpStatusCode.OK), settings.ToString());
                Assert.That(settings["set"]!.Value<Boolean>(),                                   Is.True);
                Assert.That(settings["trustAnchor"]!["roamingNetworkId"]!.Value<String>(),       Is.EqualTo("QA"));
                Assert.That(settings["trustAnchor"]!["urls"]!.Values<String>(),                  Is.EqualTo(new[] { network.URL }));
                Assert.That(settings["trustAnchor"]!["holders"]![0]!.Values<String>(),           Is.EqualTo(network.TrustAnchor.Keys));

            }

            var file = JObject.Parse(File.ReadAllText(Path.Combine(directory, WWCPConfigFile.DefaultFileName)));

            Assert.That(file["roamingNetwork"]?["trustAnchor"]?["@id"]?.Value<String>(),          Is.EqualTo("QA"), "it was not written into the configuration file, as its JSON");
            Assert.That(file["nts"]?["groups"]?["legal"]?["enabled"]?.Value<Boolean>(),           Is.False,         "the rest of the configuration file was not kept");

            (status, settings) = await Send(admin, HttpMethod.Put, "api/v1/configuration/roamingNetwork", new { trustAnchor = "" });

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.OK), settings.ToString());
            Assert.That(settings["set"]!.Value<Boolean>(),  Is.False);

            (status, var map) = await Send(driver, HttpMethod.Get, "api/v1/map");

            Assert.That(map["source"]!.Value<String>(),  Is.EqualTo("demo"), "without a roaming network, the map shows its demo stations");

        }

        #endregion

        #region SettingATrustAnchorTrustsNoKeyYet()

        /// <summary>
        /// A trust anchor set trusts none of its identity keys: no proof holds,
        /// and why is said. The key statement of the network's operator shows
        /// its keys and that it is signed validly; an administrator - and no
        /// driver - trusts a key the anchor names, and no other, and the proofs
        /// hold, until the key is no longer trusted. The decision is kept in
        /// the configuration file: who trusted the key, and since when.
        /// </summary>
        [Test]
        public async Task SettingATrustAnchorTrustsNoKeyYet()
        {

            network = await TestRoamingNetwork.Start();

            await TestPorts.StartedOnFreshPorts(() => SmartphoneFrom());

            using var admin   = await SignedInAs("admin1",  "systemadmin");
            using var driver  = await SignedInAs("driver1", "driver");

            var keyId                = network.TrustAnchor.Keys[0];
            var keyPath              = $"api/v1/configuration/roamingNetwork/trustedKeys/{keyId}";
            var checkPath            = $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check";

            var (status, settings)   = await Send(admin, HttpMethod.Put, "api/v1/configuration/roamingNetwork", new { trustAnchor = network.TrustAnchor.ToQRCode() });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                                       Is.EqualTo(HttpStatusCode.OK), settings.ToString());
                Assert.That(settings["trustedKeys"]!.Count(),             Is.Zero);
                Assert.That(settings["trustedHolders"]!.Value<Int32>(),   Is.Zero);
                Assert.That(settings["quorumTrusted"]!.Value<Boolean>(),  Is.False);
            }

            (status, var check)      = await Send(driver, HttpMethod.Get, checkPath);

            var station              = (JObject) check["stations"]![0]!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.OK), check.ToString());
                Assert.That(station["verified"]!.Value<Boolean>(),                  Is.False);
                Assert.That(station["error"]!.Value<String>(),                      Does.Contain("none of the identity keys of the trust anchor of 'QA' is trusted here yet"));
                Assert.That(station["trust"]!["signaturesValid"]!.Value<Boolean>(), Is.True);
                Assert.That(station["trust"]!["quorumReached"]!.Value<Boolean>(),   Is.False);
            }

            // What the administrator looks at before trusting a key.
            (status, var keyStatement) = await Send(driver, HttpMethod.Get, "api/v1/configuration/roamingNetwork/keyStatement");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                                                                         Is.EqualTo(HttpStatusCode.OK), keyStatement.ToString());
                Assert.That(keyStatement["from"]!.Value<String>(),                                          Is.EqualTo(network.URL));
                Assert.That(keyStatement["keyStatement"]!["operatorId"]!.Value<String>(),                   Is.EqualTo("OCC"));
                Assert.That(keyStatement["keyStatement"]!["identityKeys"]!.Select(key => key["id"]!.Value<String>()), Is.EqualTo(new[] { keyId }));
                Assert.That(keyStatement["keyStatement"]!["signedBy"]!.Values<String>(),                    Is.EqualTo(new[] { keyId }));
                Assert.That(keyStatement["evaluation"]!["canTrustKeys"]!.Value<Boolean>(),                  Is.True);
                Assert.That(keyStatement["evaluation"]!["quorumReached"]!.Value<Boolean>(),                 Is.False);
            }

            (status, var refused)    = await Send(driver, HttpMethod.Put, keyPath);

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), refused.ToString());

            (status, var unknown)    = await Send(admin, HttpMethod.Put, $"api/v1/configuration/roamingNetwork/trustedKeys/{new String('0', 64)}");

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.NotFound), unknown.ToString());
            Assert.That(unknown["error"]!.Value<String>(),  Does.Contain("names no identity key"));

            (status, settings)       = await Send(admin, HttpMethod.Put, keyPath);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                                                 Is.EqualTo(HttpStatusCode.OK), settings.ToString());
                Assert.That(settings["trustedKeys"]![0]!["id"]!.Value<String>(),         Is.EqualTo(keyId));
                Assert.That(settings["trustedKeys"]![0]!["acceptedBy"]!.Value<String>(), Is.EqualTo("admin1"));
                Assert.That(settings["quorumTrusted"]!.Value<Boolean>(),            Is.True);
            }

            var file = JObject.Parse(File.ReadAllText(Path.Combine(directory, WWCPConfigFile.DefaultFileName)));

            Assert.That(file["roamingNetwork"]?["trustedKeys"]?[0]?["id"]?.Value<String>(), Is.EqualTo(keyId), "the decision was not written into the configuration file");

            (status, check)          = await Send(driver, HttpMethod.Get, checkPath);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(check["verified"]!.Value<Boolean>(),                                 Is.True, check.ToString());
                Assert.That(check["stations"]![0]!["trust"]!["attestedBy"]!.Count(),             Is.EqualTo(1));
                Assert.That(check["stations"]![0]!["trust"]!["validUntil"]!.Type,                Is.Not.EqualTo(JTokenType.Null));
            }

            (status, keyStatement)   = await Send(driver, HttpMethod.Get, "api/v1/configuration/roamingNetwork/keyStatement");

            Assert.That(keyStatement["evaluation"]!["quorumReached"]!.Value<Boolean>(), Is.True, keyStatement.ToString());

            (status, settings)       = await Send(admin, HttpMethod.Delete, keyPath);

            Assert.That(status,                           Is.EqualTo(HttpStatusCode.OK), settings.ToString());
            Assert.That(settings["trustedKeys"]!.Count(), Is.Zero);

            (status, check)          = await Send(driver, HttpMethod.Get, checkPath);

            Assert.That(check["verified"]!.Value<Boolean>(), Is.False, "a key no longer trusted still holds");

        }

        #endregion

        #region AnotherTrustAnchorTakesNoTrustAlong()

        /// <summary>
        /// The keys trusted stay trusted where the same trust anchor is set
        /// again, in another form; another anchor - another network, holders,
        /// quorum or URLs - trusts none of them, and trusts no key it does not
        /// name.
        /// </summary>
        [Test]
        public async Task AnotherTrustAnchorTakesNoTrustAlong()
        {

            network  = await TestRoamingNetwork.Start();
            other    = await TestRoamingNetwork.Start();

            using var admin = await WithTheRoamingNetwork();

            var (status, settings)  = await Send(admin, HttpMethod.Get, "api/v1/configuration/roamingNetwork");
            var trustedSince        = settings["trustedKeys"]![0]!["acceptedAt"]!.Value<String>();

            (status, settings)      = await Send(admin, HttpMethod.Put, "api/v1/configuration/roamingNetwork", new { trustAnchor = network.TrustAnchor.ToURL() });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                                                  Is.EqualTo(HttpStatusCode.OK), settings.ToString());
                Assert.That(settings["trustedKeys"]![0]!["id"]!.Value<String>(),          Is.EqualTo(network.TrustAnchor.Keys[0]));
                Assert.That(settings["trustedKeys"]![0]!["acceptedAt"]!.Value<String>(),  Is.EqualTo(trustedSince), "trusted anew");
            }

            (status, var wrong)     = await Send(admin, HttpMethod.Put, "api/v1/configuration/roamingNetwork",
                                                 new { trustAnchor = other.TrustAnchor.ToQRCode(), trustedKeys = network.TrustAnchor.Keys.ToArray() });

            Assert.That(status,                           Is.EqualTo(HttpStatusCode.BadRequest), wrong.ToString());
            Assert.That(wrong["error"]!.Value<String>(),  Does.Contain("names no identity key"));

            (status, settings)      = await Send(admin, HttpMethod.Put, "api/v1/configuration/roamingNetwork", new { trustAnchor = other.TrustAnchor.ToQRCode() });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                                       Is.EqualTo(HttpStatusCode.OK), settings.ToString());
                Assert.That(settings["trustedKeys"]!.Count(),             Is.Zero, "another trust anchor took the decisions of the one before along");
                Assert.That(settings["quorumTrusted"]!.Value<Boolean>(),  Is.False);
            }

        }

        #endregion

        #region TheKeysTrustedAreReadFromTheConfigurationFile()

        /// <summary>
        /// A smartphone started with a "roamingNetwork" section that trusts the
        /// identity key of its anchor checks the stations against it at once.
        /// </summary>
        [Test]
        public async Task TheKeysTrustedAreReadFromTheConfigurationFile()
        {

            network = await TestRoamingNetwork.Start();

            var configuration = JObject.Parse(NoTimeServers);

            configuration["roamingNetwork"] = new JObject(
                                                  new JProperty("trustAnchor",  network.TrustAnchor.ToJSON()),
                                                  new JProperty("trustedKeys",  new JArray(new JObject(
                                                      new JProperty("id",          network.TrustAnchor.Keys[0]),
                                                      new JProperty("acceptedAt",  "2026-11-01T08:00:00.0000000+00:00"),
                                                      new JProperty("acceptedBy",  "root")
                                                  )))
                                              );

            await TestPorts.StartedOnFreshPorts(() => SmartphoneFrom(configuration.ToString()));

            using var viewer = await SignedInAs("viewer1", "viewer");

            var (status, settings)  = await Send(viewer, HttpMethod.Get, "api/v1/configuration/roamingNetwork");
            (status, var check)     = await Send(viewer, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(settings["trustedKeys"]![0]!["acceptedBy"]!.Value<String>(),  Is.EqualTo("root"), settings.ToString());
                Assert.That(settings["quorumTrusted"]!.Value<Boolean>(),                  Is.True);
                Assert.That(check["verified"]!.Value<Boolean>(),                          Is.True, check.ToString());
            }

        }

        #endregion

        #region TheMapShowsThePoolsOfTheRoamingNetwork()

        /// <summary>
        /// With a trust anchor, the map shows the charging pools the roaming
        /// network lists - where they are and their EVSEs, each with the
        /// station whose proof is to be checked - and nothing yet of what they
        /// cost, which only a proof says.
        /// </summary>
        [Test]
        public async Task TheMapShowsThePoolsOfTheRoamingNetwork()
        {

            await WithTheRoamingNetwork();

            using var driver = await SignedInAs("driver1", "driver");

            var (status, map) = await Send(driver, HttpMethod.Get, "api/v1/map");

            Assert.That(status,                    Is.EqualTo(HttpStatusCode.OK), map.ToString());
            Assert.That(map["locations"]!.Count(), Is.EqualTo(1),                  map.ToString());

            var location = (JObject) map["locations"]!.Single();
            var evses    = (JArray)  location["evses"]!;

            using (Assert.EnterMultipleScope())
            {

                Assert.That(map["source"]!.Value<String>(),                           Is.EqualTo("roamingNetwork"));
                Assert.That(map["roamingNetwork"]!["id"]!.Value<String>(),            Is.EqualTo("QA"));
                Assert.That(map["roamingNetwork"]!["from"]!.Value<String>(),          Is.EqualTo(network!.URL));

                Assert.That(location["id"]!.Value<String>(),                          Is.EqualTo(TestRoamingNetwork.PoolId));
                Assert.That(location["name"]!.Value<String>(),                        Is.EqualTo("Jena, Paradiesbahnhof"));
                Assert.That(location["address"]!.Value<String>(),                     Is.EqualTo("Am Paradiesbahnhof 1"));
                Assert.That(location["city"]!.Value<String>(),                        Is.EqualTo("Jena"));
                Assert.That(location["operator"]!["name"]!.Value<String>(),           Is.EqualTo("GraphDefined Charging"));
                Assert.That(location["coordinates"]!["latitude"]!.Value<String>(),    Is.EqualTo("50.922100"));
                Assert.That(location["tariff"]!.Type,                                 Is.EqualTo(JTokenType.Null), "a price before any proof was checked");
                Assert.That(location["verified"]!.Type,                               Is.EqualTo(JTokenType.Null), "checked before anybody asked");

                Assert.That(evses.Select(evse => evse["evse_id"]!.Value<String>()),     Is.EqualTo(new[] { TestRoamingNetwork.ACEVSE, TestRoamingNetwork.DCEVSE }));
                Assert.That(evses.Select(evse => evse["station_id"]!.Value<String>()),  Is.EqualTo(new[] { TestRoamingNetwork.ACStation, TestRoamingNetwork.DCStation }));
                // As the network says it over its WebSocket - or not known,
                // where the phone does not follow it yet.
                Assert.That(evses.Select(evse => evse["status"]!.Value<String>()),      Is.All.EqualTo(StatusLive(map) ? "AVAILABLE" : "UNKNOWN"));
                Assert.That(evses[1]!["connectors"]![0]!["power_type"]!.Value<String>(),          Is.EqualTo("DC"));
                Assert.That(evses[1]!["connectors"]![0]!["max_electric_power"]!.Value<Int64>(),   Is.EqualTo(150000));
                Assert.That(evses[0]!["connectors"]![0]!["standard"]!.Value<String>(),            Is.EqualTo("IEC_62196_T2"));

            }

        }

        #endregion

        #region AStationIsCheckedAgainstTheTrustAnchor()

        /// <summary>
        /// Opened, a pool's stations are checked: each proof attested by the
        /// operator of the network as its trust anchor names it, the price
        /// statement of the DC EVSE signed by its operator - and what the proofs
        /// give, its tariffs and the public key each energy meter signs its
        /// measurements with, exactly the one the meter has.
        /// </summary>
        [Test]
        public async Task AStationIsCheckedAgainstTheTrustAnchor()
        {

            await WithTheRoamingNetwork();

            using var viewer = await SignedInAs("viewer1", "viewer");

            var (status, check) = await Send(viewer, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), check.ToString());

            var stations  = ((JArray) check["stations"]!).OfType<JObject>().ToDictionary(station => station["stationId"]!.Value<String>()!);
            var ac        = stations[TestRoamingNetwork.ACStation];
            var dc        = stations[TestRoamingNetwork.DCStation];
            var acEVSE    = (JObject) ac["evses"]!.Single();
            var dcEVSE    = (JObject) dc["evses"]!.Single();

            using (Assert.EnterMultipleScope())
            {

                Assert.That(check["verified"]!.Value<Boolean>(),                                 Is.True, check.ToString());
                Assert.That(ac["verified"]!.Value<Boolean>(),                                    Is.True, ac["error"]?.ToString());
                Assert.That(dc["verified"]!.Value<Boolean>(),                                    Is.True, dc["error"]?.ToString());

                Assert.That(dc["roamingNetwork"]!["name"]!.Value<String>(),                      Is.EqualTo("Quality Assurance"));
                Assert.That(dc["roamingNetwork"]!["operator"]!.Value<String>(),                  Is.EqualTo("Open Charging Cloud"));
                Assert.That(dc["attestation"]!["role"]!.Value<String>(),                         Is.EqualTo("roamingNetworkOperator"));
                Assert.That(dc["chargingStationOperator"]!["id"]!.Value<String>(),               Is.EqualTo("DE*GEF"));

                // Where to pay ad hoc: the DC station's own page, of the AC
                // station - naming none, nor its pool - that of its operator.
                Assert.That(dc["webPayment"]!["url"]!.Value<String>(),                           Is.EqualTo(TestRoamingNetwork.DCWebPaymentURL));
                Assert.That(dc["webPayment"]!["of"]!. Value<String>(),                           Is.EqualTo("chargingStation"));
                Assert.That(ac["webPayment"]!["url"]!.Value<String>(),                           Is.EqualTo(TestRoamingNetwork.WebPaymentURL));
                Assert.That(ac["webPayment"]!["of"]!. Value<String>(),                           Is.EqualTo("chargingStationOperator"));

                Assert.That(dcEVSE["id"]!.Value<String>(),                                       Is.EqualTo(TestRoamingNetwork.DCEVSE));
                Assert.That(dcEVSE["maxPowerKW"]!.Value<Decimal>(),                              Is.EqualTo(150m));
                Assert.That(dcEVSE["tariffs"]![0]!["id"]!.Value<String>(),                       Is.EqualTo(TestRoamingNetwork.DCTariff));

                // The tariff says 0.69, the statement of the EVSE 0.65: the
                // statement wins. The AC EVSE has none, and keeps its tariff.
                Assert.That(dcEVSE["price"]!["pricePerKWh"]!.Value<Decimal>(),                   Is.EqualTo(0.65m));
                Assert.That(acEVSE["price"]!["pricePerKWh"]!.Value<Decimal>(),                   Is.EqualTo(0.49m));
                Assert.That(acEVSE["price"]!["currency"]!.Value<String>(),                       Is.EqualTo("EUR"));

                Assert.That(dc["priceStatements"]!.Single()["verified"]!.Value<Boolean>(),       Is.True, dc["priceStatements"]!.ToString());
                Assert.That(dc["priceStatements"]!.Single()["current"]!.Value<Boolean>(),        Is.True, "the one statement of the EVSE is not the one in force");
                Assert.That(dc["priceStatements"]!.Single()["signer"]!.Value<String>(),          Is.EqualTo("DE*GEF"));
                Assert.That(dc["priceConflict"]!.Type,                                           Is.EqualTo(JTokenType.Null));

                // The key to check the meter values of a session at the DC
                // EVSE with - known before the first of them.
                Assert.That(dcEVSE["energyMeter"]!["id"]!.Value<String>(),                       Is.EqualTo("meter-dc-1"));
                Assert.That(dcEVSE["energyMeter"]!["serialNumber"]!.Value<String>(),             Is.EqualTo("DC-2026-001"));
                Assert.That(dcEVSE["energyMeter"]!["signatureMethods"]!.Values<String>(),        Is.EqualTo(new[] { "OCMF" }));
                Assert.That(dcEVSE["energyMeter"]!["publicKeys"]![0]!["key"]!.Value<String>(),   Is.EqualTo(network!.DCMeterKey));
                Assert.That(dcEVSE["energyMeter"]!["publicKeys"]![0]!["algorithm"]!.Value<String>(),  Is.EqualTo("secp256r1"));
                Assert.That(acEVSE["energyMeter"]!["publicKeys"]![0]!["key"]!.Value<String>(),   Is.EqualTo(network.ACMeterKey));

            }

            // Checked, the map shows what it costs and that it holds.
            (status, var map) = await Send(viewer, HttpMethod.Get, "api/v1/map");

            var location = (JObject) map["locations"]!.Single();

            Assert.That(location["verified"]!.Value<Boolean>(),                Is.True);
            Assert.That(location["tariff"]!["price_per_kwh"]!.Value<Decimal>(), Is.EqualTo(0.49m), "the price of its first EVSE");

        }

        #endregion

        #region TheTrustAnchorOfAnotherNetworkHoldsNothingHere()

        /// <summary>
        /// A trust anchor naming the URL of this network and the identity key
        /// of another operator - a QR code that is not what it says - finds the
        /// stations, and no proof of them holds: nothing of their tariffs or
        /// meters is shown, and nothing is charged there.
        /// </summary>
        [Test]
        public async Task TheTrustAnchorOfAnotherNetworkHoldsNothingHere()
        {

            network  = await TestRoamingNetwork.Start();
            other    = await TestRoamingNetwork.Start();

            var forged = new RoamingNetworkTrustAnchor(network.TrustAnchor.Id, network.TrustAnchor.URLs, other.TrustAnchor.Keys);

            await WithTheRoamingNetwork(forged);

            using var driver = await SignedInAs("driver1", "driver");

            var (status, map) = await Send(driver, HttpMethod.Get, "api/v1/map");

            Assert.That(map["locations"]!.Count(), Is.EqualTo(1), "the list of pools is not signed, and only says where to look");

            (status, var check) = await Send(driver, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            var station = (JObject) check["stations"]![0]!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                                 Is.EqualTo(HttpStatusCode.OK), check.ToString());
                Assert.That(check["verified"]!.Value<Boolean>(),    Is.False);
                Assert.That(station["verified"]!.Value<Boolean>(),  Is.False);
                Assert.That(station["error"]!.Value<String>(),      Does.Contain("not attested by the operator the trust anchor names"));
                Assert.That(station["evses"],                       Is.Null, "what a proof that does not hold gives is shown");
            }

            (status, var refused) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.ACEVSE });

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.Conflict), refused.ToString());
            Assert.That(refused["error"]!.Value<String>(),  Does.Contain("is not signed as the trust anchor of the roaming network requires"));

        }

        #endregion

        #region AnAttestationThatEndedHoldsNothing()

        /// <summary>
        /// A network whose operator attested its head an hour ago, for a quarter
        /// of an hour: what it serves is attested by nobody now.
        /// </summary>
        [Test]
        public async Task AnAttestationThatEndedHoldsNothing()
        {

            network = await TestRoamingNetwork.Start(DateTimeOffset.UtcNow.AddHours(-1));

            using var admin = await WithTheRoamingNetwork();

            var (status, check) = await Send(admin, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            Assert.That(status,                                                Is.EqualTo(HttpStatusCode.OK), check.ToString());
            Assert.That(check["verified"]!.Value<Boolean>(),                   Is.False);
            Assert.That(check["stations"]![0]!["error"]!.Value<String>(),      Does.Contain("not attested"));

        }

        #endregion

        #region AMirrorIsAskedWhereTheHubIsDown()

        /// <summary>
        /// A trust anchor naming a URL nobody answers at first and the network's
        /// own second: the second is asked, for the pools and for the proofs.
        /// </summary>
        [Test]
        public async Task AMirrorIsAskedWhereTheHubIsDown()
        {

            network = await TestRoamingNetwork.Start();

            var down    = URL.Parse($"http://127.0.0.1:{TestPorts.Free()}/RNs/QA");
            var anchor  = new RoamingNetworkTrustAnchor(network.TrustAnchor.Id, [ down, network.TrustAnchor.URLs[0] ], network.TrustAnchor.Keys);

            using var admin = await WithTheRoamingNetwork(anchor);

            var (_, map)    = await Send(admin, HttpMethod.Get, "api/v1/map");
            var (_, check)  = await Send(admin, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(map["roamingNetwork"]!["from"]!.Value<String>(),        Is.EqualTo(network.URL));
                Assert.That(map["locations"]!.Count(),                              Is.EqualTo(1));
                Assert.That(check["verified"]!.Value<Boolean>(),                    Is.True, check.ToString());
                Assert.That(check["stations"]![0]!["from"]!.Value<String>(),        Is.EqualTo(network.URL));
            }

        }

        #endregion

        #region ADriverChargesAtACheckedStationForThePriceItsProofGives()

        /// <summary>
        /// "Charge now!" at a station of the roaming network: refused until its
        /// proof was checked, and then started for the price the price statement
        /// of the EVSE sets - simulated as every session is.
        /// </summary>
        [Test]
        public async Task ADriverChargesAtACheckedStationForThePriceItsProofGives()
        {

            await WithTheRoamingNetwork();

            using var driver = await SignedInAs("driver1", "driver");

            await Send(driver, HttpMethod.Get, "api/v1/map");

            var (status, refused) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.DCEVSE });

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.Conflict), refused.ToString());
            Assert.That(refused["error"]!.Value<String>(),  Does.Contain("was not checked yet"));

            (status, var check) = await Send(driver, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            Assert.That(check["verified"]!.Value<Boolean>(), Is.True, check.ToString());

            (status, var session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.DCEVSE });

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                                          Is.EqualTo(HttpStatusCode.Created), session.ToString());
                Assert.That(session["simulated"]!.Value<Boolean>(),          Is.True);
                Assert.That(session["evse"]!["evseId"]!.Value<String>(),     Is.EqualTo(TestRoamingNetwork.DCEVSE));
                Assert.That(session["cost"]!["pricePerKWh"]!.Value<Decimal>(), Is.EqualTo(0.65m));
                Assert.That(session["location"]!["name"]!.Value<String>(),   Is.EqualTo("Jena, Paradiesbahnhof"));
            }

            (status, var map) = await Send(driver, HttpMethod.Get, "api/v1/map");

            var dc = map["locations"]![0]!["evses"]!.Single(evse => evse["evse_id"]!.Value<String>() == TestRoamingNetwork.DCEVSE);

            Assert.That(dc["status"]!.Value<String>(), Is.EqualTo("CHARGING"), "this phone charges there");

        }

        #endregion

        #region AWebPaymentPageStartsASessionThePhoneFollowsOverItsLiveLink()

        /// <summary>
        /// Paid at the web payment page of the operator, which started the
        /// session and gave its live link: handed over at the checked DC
        /// station, the session is starting until something comes over it,
        /// charging from the first signed document on, its energy what the
        /// documents say, and finished once the meter signs the end - stopped
        /// at the page, not here, and kept as it was across a restart.
        /// </summary>
        [Test]
        public async Task AWebPaymentPageStartsASessionThePhoneFollowsOverItsLiveLink()
        {

            await WithTheRoamingNetwork();

            using var driver = await SignedInAs("driver1", "driver");

            await Send(driver, HttpMethod.Get, "api/v1/map");
            await Send(driver, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            cpo = await TestCPO.Start();

            var liveLinkURL = cpo.LiveLinkURL("S1");

            var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions",
                                               new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.DCEVSE, liveLink = liveLinkURL });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            var id = session["id"]!.Value<String>()!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(session["status"]!.Value<String>(),                     Is.EqualTo("starting"), "nothing came over it yet");
                Assert.That(session["simulated"]!.Value<Boolean>(),                 Is.False);
                Assert.That(session["cost"]!["pricePerKWh"]!.Value<Decimal>(),      Is.EqualTo(0.65m), "the price its checked proof gives");
                Assert.That(session["liveLink"]!["from"]!.Value<String>(),          Is.EqualTo(liveLinkURL));
                Assert.That(session["liveLink"]!["evseId"]!.Value<String>(),        Is.EqualTo(TestRoamingNetwork.DCEVSE));
                Assert.That(session["liveLink"]!["transports"]!.Values<String>(),   Is.EqualTo(new[] { "https" }));
                Assert.That(session["liveLink"]!["start"]!.Type,                    Is.EqualTo(JTokenType.Null), "no start value came yet");
            }

            (status, var refused) = await Send(driver, HttpMethod.Post, $"api/v1/charging/sessions/{id}/stop");

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.Conflict), refused.ToString());
            Assert.That(refused["error"]!.Value<String>(),  Does.Contain("web payment page"));

            var begin = DateTimeOffset.UtcNow;

            cpo.Send(TestRoamingNetwork.OCMF(network!.DCMeter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (begin, "B", 1000.000m)));

            var charging = await SessionWhen(driver, id, json => json["liveLink"]?["start"] is JObject);
            var start    = charging["liveLink"]!["start"]!;
            var keyId    = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(network.DCMeterKey)));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(charging["status"]!.Value<String>(),                           Is.EqualTo("charging"));
                Assert.That(charging["energyKWh"]!.Value<Decimal>(),                       Is.EqualTo(0m));
                Assert.That(charging["liveLink"]!["documents"]!.Value<Int32>(),            Is.EqualTo(1));
                Assert.That(charging["liveLink"]!["readings"]!.Count(),                    Is.EqualTo(1));
                Assert.That(charging["liveLink"]!["lastTransport"]!.Value<String>(),       Is.EqualTo("https"));
                Assert.That(charging["liveLink"]!["lastEndpoint"]!.Value<String>(),        Is.EqualTo($"{cpo.URL}/sessions/S1/live"));
                Assert.That(charging["liveLink"]!["lastKind"]!.Value<String>(),            Does.Contain("ChargeTransparencyRecord"));
                Assert.That(start["holds"]!.Value<Boolean?>(),                             Is.True, start.ToString());
                Assert.That(start["error"]!.Type,                                          Is.EqualTo(JTokenType.Null));
                Assert.That(start["result"]!.Value<String>(),                              Is.EqualTo("ValidSignature"));
                Assert.That(start["meterSerialNumber"]!.Value<String>(),                   Is.EqualTo(TestRoamingNetwork.DCMeterSerial));
                Assert.That(start["meterId"]!.Value<String>(),                             Is.EqualTo("meter-dc-1"));
                Assert.That(start["keyId"]!.Value<String>(),                               Is.EqualTo(keyId), "checked with another key than the one the proof gave");
                Assert.That(start["reading"]!["energyKWh"]!.Value<Decimal>(),              Is.EqualTo(1000m));
                Assert.That(start["reading"]!["transaction"]!.Value<String>(),             Is.EqualTo("B"));
            }

            cpo.Send(TestRoamingNetwork.OCMF(network.DCMeter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (DateTimeOffset.UtcNow, "C", 1012.500m)));

            charging = await SessionWhen(driver, id, json => json["liveLink"]?["documents"]?.Value<Int32>() == 2);

            Assert.That(charging["energyKWh"]!.Value<Decimal>(), Is.EqualTo(12.5m), charging.ToString());

            cpo.Send(TestRoamingNetwork.OCMF(network.DCMeter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (DateTimeOffset.UtcNow, "E", 1020.000m)));

            var finished = await SessionWhen(driver, id, json => json["status"]?.Value<String>() == "finished");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(finished["energyKWh"]!.Value<Decimal>(),                       Is.EqualTo(20m));
                Assert.That(finished["cost"]!["amount"]!.Value<Decimal>(),                 Is.EqualTo(13.00m));
                Assert.That(finished["liveLink"]!["documents"]!.Value<Int32>(),            Is.EqualTo(3));
                Assert.That(finished["liveLink"]!["endedBecause"]!.Value<String>(),        Does.Contain("end of the transaction"));
                Assert.That(finished["endedAt"]?.Type,                                     Is.AnyOf(JTokenType.String, JTokenType.Date), "when it ended");
            }

            // Followed no more once it ended.
            await Task.Delay(500);

            var asked = cpo.LiveRequests;

            await Task.Delay(1000);

            Assert.That(cpo.LiveRequests, Is.EqualTo(asked), "its live link is not asked any more");

            var configuration = File.ReadAllText(Path.Combine(directory, WWCPConfigFile.DefaultFileName));

            await smartphone!.DisposeAsync();
            smartphone = null;

            await TestPorts.StartedOnFreshPorts(() => SmartphoneFrom(configuration));

            using var again = await SignedInAs("driver2", "driver");

            (status, var read) = await Send(again, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(read["status"]!.Value<String>(),                        Is.EqualTo("finished"), read.ToString());
                Assert.That(read["energyKWh"]!.Value<Decimal>(),                    Is.EqualTo(20m));
                Assert.That(read["liveLink"]!["documents"]!.Value<Int32>(),         Is.EqualTo(3));
                Assert.That(read["liveLink"]!["from"]!.Value<String>(),             Is.EqualTo(liveLinkURL));
                Assert.That(read["liveLink"]!["start"]!["holds"]!.Value<Boolean?>(), Is.True, "how its start value held was not kept");
            }

        }

        #endregion

        #region AWrongSignedStartValueStopsTheSession()

        /// <summary>
        /// A signed start value the meter's key, as the proof of the station
        /// gives it, did not sign, one of another meter, and one the meter
        /// itself says is not good are wrong start values: the phone stops the
        /// session, says why, follows its live link no more - and the session
        /// stays stopped across a restart.
        /// </summary>
        [Test]
        public async Task AWrongSignedStartValueStopsTheSession()
        {

            await WithTheRoamingNetwork();

            using var driver = await SignedInAs("driver1", "driver");

            await Send(driver, HttpMethod.Get, "api/v1/map");
            await Send(driver, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            cpo = await TestCPO.Start();

            var begin = DateTimeOffset.UtcNow;

            var cases = new (String Session, String Start, String Result, String Error)[] {
                            ("S1", TestRoamingNetwork.OCMF        (TestRoamingNetwork.NewMeterKey(), TestRoamingNetwork.DCMeterSerial, "web-payment-1",      (begin, "B", 1000.000m)),  "InvalidSignature", "is not signed by the key"),
                            ("S2", TestRoamingNetwork.OCMF        (network!.DCMeter,                 "AC-2026-001",                    "web-payment-2",      (begin, "B", 1000.000m)),  "",                 $"It is of the meter AC-2026-001, and the proof of {TestRoamingNetwork.DCStation} names the meter {TestRoamingNetwork.DCMeterSerial} for {TestRoamingNetwork.DCEVSE}"),
                            ("S3", TestRoamingNetwork.OCMFOfStatus(network.DCMeter,                  TestRoamingNetwork.DCMeterSerial, "web-payment-3", "M", (begin, "B", 1000.000m)),  "ValidSignature",   "its status is \"M\" (manipulated)")
                        };

            var stopped = new List<String>();

            foreach (var (name, startValue, result, error) in cases)
            {

                // What the station sends, before the phone asks for it.
                cpo.Send(startValue);

                var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions",
                                                   new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.DCEVSE, liveLink = cpo.LiveLinkURL(name) });

                Assert.That(status, Is.EqualTo(HttpStatusCode.Created), $"{name}: {session}");

                var id     = session["id"]!.Value<String>()!;
                var wrong  = await SessionWhen(driver, id, json => json["status"]?.Value<String>() == "stopped");
                var start  = wrong["liveLink"]!["start"]!;

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(start["holds"]!.Value<Boolean?>(),             Is.False,                         $"{name}: {start}");
                    Assert.That(start["error"]!.Value<String>(),               Does.Contain(error),              name);
                    Assert.That(start["result"]!.Value<String>() ?? "",        Is.EqualTo(result),               name);
                    Assert.That(wrong["stoppedBecause"]!.Value<String>(),      Does.Contain("signed start meter value is a wrong one"), name);
                    Assert.That(wrong["endedAt"]?.Type,                        Is.AnyOf(JTokenType.String, JTokenType.Date), $"{name}: when it was stopped");
                    Assert.That(wrong["energyKWh"]!.Value<Decimal>(),          Is.EqualTo(0m),                   name);
                }

                stopped.Add(id);

            }

            // Followed no more once it was stopped.
            await Task.Delay(500);

            var asked = cpo.LiveRequests;

            await Task.Delay(1000);

            Assert.That(cpo.LiveRequests, Is.EqualTo(asked), "a live link of a stopped session is still asked");

            var configuration = File.ReadAllText(Path.Combine(directory, WWCPConfigFile.DefaultFileName));

            await smartphone!.DisposeAsync();
            smartphone = null;

            await TestPorts.StartedOnFreshPorts(() => SmartphoneFrom(configuration));

            using var again = await SignedInAs("driver2", "driver");

            foreach (var id in stopped)
            {

                var (_, read) = await Send(again, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(read["status"]!.Value<String>(),                          Is.EqualTo("stopped"), read.ToString());
                    Assert.That(read["liveLink"]!["start"]!["holds"]!.Value<Boolean?>(),  Is.False);
                }

            }

            await Task.Delay(1000);

            Assert.That(cpo.LiveRequests, Is.EqualTo(asked), "a live link of a stopped session is asked again after a restart");

        }

        #endregion

        #region AStartValueNoProofKeptCoversStopsNothing()

        /// <summary>
        /// A signed start value of a time no proof kept of the station covers
        /// is not checked - this phone can not decide - and stops nothing: the
        /// session charges on, its start checked again with every update, and
        /// said once.
        /// </summary>
        [Test]
        public async Task AStartValueNoProofKeptCoversStopsNothing()
        {

            await WithTheRoamingNetwork();

            using var driver = await SignedInAs("driver1", "driver");

            await Send(driver, HttpMethod.Get, "api/v1/map");
            await Send(driver, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            cpo = await TestCPO.Start();

            var begin = DateTimeOffset.UtcNow.AddHours(-2);

            cpo.Send(TestRoamingNetwork.OCMF(network!.DCMeter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (begin, "B", 1000.000m)));

            var (status, session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions",
                                               new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.DCEVSE, liveLink = cpo.LiveLinkURL("S1") });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            var id         = session["id"]!.Value<String>()!;
            var undecided  = await SessionWhen(driver, id, json => json["liveLink"]?["start"] is JObject);
            var start      = undecided["liveLink"]!["start"]!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(start["holds"]!.Type,                         Is.EqualTo(JTokenType.Null), start.ToString());
                Assert.That(start["error"]!.Value<String>(),              Does.Contain($"No key of the meter {TestRoamingNetwork.DCMeterSerial}"));
                Assert.That(start["result"]!.Type,                        Is.EqualTo(JTokenType.Null), "checked with no key");
                Assert.That(undecided["status"]!.Value<String>(),         Is.EqualTo("charging"));
            }

            cpo.Send(TestRoamingNetwork.OCMF(network.DCMeter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (DateTimeOffset.UtcNow.AddSeconds(-1), "C", 1004.000m)));

            var charging = await SessionWhen(driver, id, json => json["liveLink"]?["documents"]?.Value<Int32>() == 2);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(charging["status"]!.Value<String>(),                     Is.EqualTo("charging"), charging.ToString());
                Assert.That(charging["energyKWh"]!.Value<Decimal>(),                 Is.EqualTo(4m));
                Assert.That(charging["liveLink"]!["start"]!["holds"]!.Type,          Is.EqualTo(JTokenType.Null));
                Assert.That(charging["liveLink"]!["start"]!["checkedAt"]!.ToString(), Is.EqualTo(start["checkedAt"]!.ToString()), "the same reason kept again");
            }

        }

        #endregion

        #region ALiveLinkIsTakenOnlyAtTheChargePointItIsOf()

        /// <summary>
        /// A live link is taken at a station whose proof was checked, of the
        /// charge point it is handed over at, and only where it is one: by an
        /// https URL - or http on the loopback - that answers, or as its
        /// document itself.
        /// </summary>
        [Test]
        public async Task ALiveLinkIsTakenOnlyAtTheChargePointItIsOf()
        {

            await WithTheRoamingNetwork();

            using var driver = await SignedInAs("driver1", "driver");

            cpo = await TestCPO.Start();

            await Send(driver, HttpMethod.Get, "api/v1/map");

            Task<(HttpStatusCode Status, JObject JSON)> HandOver(Object LiveLink)
                => Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.DCEVSE, liveLink = LiveLink });

            var (status, refused) = await HandOver(cpo.LiveLinkURL("S1"));

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.Conflict), refused.ToString());
            Assert.That(refused["error"]!.Value<String>(),  Does.Contain("was not checked yet"));

            await Send(driver, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            var cases = new (Object LiveLink, HttpStatusCode Status, String Error)[] {
                            (cpo.LiveLink("S1", TestRoamingNetwork.ACEVSE),  HttpStatusCode.Conflict,    $"is of {TestRoamingNetwork.ACEVSE}, not of {TestRoamingNetwork.DCEVSE}"),
                            ("http://pay.gef.example/liveLinks/S1",          HttpStatusCode.BadRequest,  "neither the https URL of a live link nor its document"),
                            (new JObject(new JProperty("@context", "x")),    HttpStatusCode.BadRequest,  "is no charge transparency live link"),
                            ("{ \"created\": ",                             HttpStatusCode.BadRequest,  "is no JSON document"),
                            ($"{cpo.URL}/nothing/here",                      HttpStatusCode.BadGateway,  "not the live link")
                        };

            foreach (var (liveLink, expected, error) in cases)
            {
                (status, refused) = await HandOver(liveLink);
                Assert.That(status,                             Is.EqualTo(expected), $"{liveLink}: {refused}");
                Assert.That(refused["error"]!.Value<String>(),  Does.Contain(error),  liveLink.ToString());
            }

            (status, var session) = await HandOver(cpo.LiveLink("S1").ToString());

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status,                                         Is.EqualTo(HttpStatusCode.Created), session.ToString());
                Assert.That(session["liveLink"]!["from"]!.Type,             Is.EqualTo(JTokenType.Null), "its document handed over, not its URL");
            }

            (status, refused) = await HandOver(cpo.LiveLinkURL("S2"));

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.Conflict), refused.ToString());
            Assert.That(refused["error"]!.Value<String>(),  Does.Contain("already charging"));

        }

        #endregion

        #region AStartThroughTheProviderNamesWhatTheDriverSaw()

        /// <summary>
        /// With an account at an e-mobility provider, the start request at a
        /// checked station names what the driver saw: the version of the
        /// station's proof the phone checked, by the digest of its effective
        /// tree, and the tariff its price is of.
        /// </summary>
        [Test]
        public async Task AStartThroughTheProviderNamesWhatTheDriverSaw()
        {

            emsp = await TestEMSP.Start();

            using var admin  = await WithTheRoamingNetwork();
            using var driver = await SignedInAs("driver1", "driver");

            var (status, signedUp) = await Send(admin, HttpMethod.Post, "api/v1/provider/signup", new { url = emsp.URL, username = "phone1", email = "phone1@example.org" });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), signedUp.ToString());

            (status, var check) = await Send(driver, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            Assert.That(check["verified"]!.Value<Boolean>(), Is.True, check.ToString());

            var effectiveTree = check["stations"]!.Single(station => station["stationId"]!.Value<String>() == TestRoamingNetwork.DCStation)!["roamingNetwork"]!["effectiveTree"]!.Value<String>();

            (status, var session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.DCEVSE });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Created), session.ToString());

            var payload = CBORValue.Parse(COSESign.Parse(emsp.Starts.Single().Request).Payload!);

            CBORValue Of(String Key) => payload.TryGetValue(CBORValue.FromText(Key), out var value) ? value : throw new AssertionException($"'{Key}' is not in the start request.");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(effectiveTree,                                   Has.Length.EqualTo(64), check.ToString());
                Assert.That(Convert.ToHexStringLower(Of("proof").AsBytes()), Is.EqualTo(effectiveTree), "the start names another version than the one checked");
                Assert.That(Of("tariff").AsText(),                           Is.EqualTo(TestRoamingNetwork.DCTariff));
                Assert.That(Of("evse").AsText(),                             Is.EqualTo(TestRoamingNetwork.DCEVSE));
                Assert.That(session["start"]!["token"]!.Value<String>(),     Is.EqualTo(emsp.Starts.Single().Token));
                Assert.That(session["cost"]!["pricePerKWh"]!.Value<Decimal>(), Is.EqualTo(0.65m));
            }

        }

        #endregion


        #region TheMeterValuesOfAChargeDetailRecordAreTheMetersAsTheProofSaid()

        /// <summary>
        /// The signed meter values of the charge detail record of a session at
        /// a station of the roaming network are checked with the key the proofs
        /// kept of its station give for the meter of its EVSE at the times they
        /// were measured: signed by that meter, of this start - and what they
        /// say was charged is what the record says, at the price the operator
        /// stated for the EVSE and that was in force at the start. The proofs
        /// are kept with the session - the one of the start and one of its
        /// stop - and hold after a restart as they did before.
        /// </summary>
        [Test]
        public async Task TheMeterValuesOfAChargeDetailRecordAreTheMetersAsTheProofSaid()
        {

            var (admin, driver, session, token) = await ChargingThroughTheProvider();

            using var _admin   = admin;
            using var _driver  = driver;

            var id = session["id"]!.Value<String>()!;

            var (status, stopped) = await Send(driver, HttpMethod.Post, $"api/v1/charging/sessions/{id}/stop");

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), stopped.ToString());

            var begin  = StartedAt(session);
            var end    = DateTimeOffset.UtcNow;

            emsp!.OperatorCDR(token, 12.5m, 8.13m, end, SignedValues(
                ("Start",  TestRoamingNetwork.OCMF(network!.DCMeter, TestRoamingNetwork.DCMeterSerial, token, (begin, "B", 1000.000m))),
                ("End",    TestRoamingNetwork.OCMF(network.DCMeter,  TestRoamingNetwork.DCMeterSerial, token, (end,   "E", 1012.500m)))
            ));

            (status, var finished) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            var check   = finished["check"] as JObject;
            var values  = check?["meterValues"];
            var price   = check?["price"];
            var keyId   = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Convert.FromBase64String(network.DCMeterKey)));
            var kept    = JArray.Parse(File.ReadAllText(Path.Combine(directory, ChargingSessions.DefaultFileName))).Single()["evidence"];

            using (Assert.EnterMultipleScope())
            {

                Assert.That(check,                                                  Is.Not.Null, finished.ToString());
                Assert.That(check?["error"]?.Type,                                  Is.EqualTo(JTokenType.Null), check?.ToString());

                Assert.That(values?["verified"]?.Value<Boolean>(),                  Is.True,  values?.ToString());
                Assert.That(values?["values"]?.Count(),                             Is.EqualTo(2));
                Assert.That(values?["values"]?[0]?["result"]?.Value<String>(),      Is.EqualTo("ValidSignature"));
                Assert.That(values?["values"]?[1]?["meterSerialNumber"]?.Value<String>(), Is.EqualTo(TestRoamingNetwork.DCMeterSerial));
                Assert.That(values?["values"]?[1]?["meterId"]?.Value<String>(),     Is.EqualTo("meter-dc-1"));
                Assert.That(values?["values"]?[1]?["keyId"]?.Value<String>(),       Is.EqualTo(keyId), "checked with another key than the one the proof gave");
                Assert.That(values?["values"]?[1]?["ofThisStart"]?.Value<Boolean>(), Is.True, "the meter values do not name the token of the start");
                Assert.That(values?["startKWh"]?.Value<Decimal>(),                  Is.EqualTo(1000m));
                Assert.That(values?["endKWh"]?.Value<Decimal>(),                    Is.EqualTo(1012.5m));
                Assert.That(values?["energyKWh"]?.Value<Decimal>(),                 Is.EqualTo(12.5m));
                Assert.That(values?["energyMatches"]?.Value<Boolean>(),             Is.True);
                Assert.That(values?["expectedCost"]?.Value<Decimal>(),              Is.EqualTo(8.13m), "12.5 kWh at 0.65 per kWh");
                Assert.That(values?["costMatches"]?.Value<Boolean>(),               Is.True);

                Assert.That(price?["signatures"]?.Value<String>(),                  Is.EqualTo("valid"),   price?.ToString());
                Assert.That(price?["applicability"]?.Value<String>(),               Is.EqualTo("unbound"), "no assignment of the operator binds the price to the session");
                Assert.That(price?["agreement"]?.Value<String>(),                   Is.EqualTo("missing"));
                Assert.That(price?["selected"]?["entityId"]?.Value<String>(),       Is.EqualTo(TestRoamingNetwork.DCEVSE));
                Assert.That(price?["selected"]?["tariffId"]?.Value<String>(),       Is.EqualTo(TestRoamingNetwork.DCTariff));
                Assert.That(price?["selected"]?["pricePerKWh"]?.Value<Decimal>(),   Is.EqualTo(0.65m));
                Assert.That(price?["matchesSession"]?.Value<Boolean>(),             Is.True);

                Assert.That(kept?["proofs"]?.Count(),                               Is.EqualTo(2), "the proof of the start and the one of the stop are kept");
                Assert.That(kept?["priceStatements"]?.Count(),                      Is.EqualTo(1));

            }

            var configuration = File.ReadAllText(Path.Combine(directory, WWCPConfigFile.DefaultFileName));

            await smartphone!.DisposeAsync();
            smartphone = null;

            await TestPorts.StartedOnFreshPorts(() => SmartphoneFrom(configuration));

            using var again = await SignedInAs("driver2", "driver");

            (status, var read) = await Send(again, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(read["check"]?["meterValues"]?["verified"]?.Value<Boolean>(), Is.True,    read.ToString());
                Assert.That(read["check"]?["price"]?["signatures"]?.Value<String>(),      Is.EqualTo("valid"));
            }

        }

        #endregion

        #region AMeterValueTheMeterDidNotSignHoldsNothing()

        /// <summary>
        /// A meter value signed by another key than the one the proof gave for
        /// the meter of the EVSE is not the meter's, whatever it says; one of
        /// another meter, and one of a time no proof kept covers, are not
        /// checked at all - and the energy is said of none of them.
        /// </summary>
        [Test]
        public async Task AMeterValueTheMeterDidNotSignHoldsNothing()
        {

            var (admin, driver, session, token) = await ChargingThroughTheProvider();

            using var _admin   = admin;
            using var _driver  = driver;

            var id     = session["id"]!.Value<String>()!;
            var begin  = StartedAt(session);
            var end    = DateTimeOffset.UtcNow;

            emsp!.OperatorCDR(token, 12.5m, 8.13m, end, SignedValues(
                ("Start",         TestRoamingNetwork.OCMF(network!.DCMeter,                 TestRoamingNetwork.DCMeterSerial, token, (begin,                "B", 1000.000m))),
                ("End",           TestRoamingNetwork.OCMF(TestRoamingNetwork.NewMeterKey(), TestRoamingNetwork.DCMeterSerial, token, (end,                  "E", 1012.500m))),
                ("Intermediate",  TestRoamingNetwork.OCMF(network.DCMeter,                  "AC-2026-001",                    token, (end,                  "T", 1010.000m))),
                ("Intermediate",  TestRoamingNetwork.OCMF(network.DCMeter,                  TestRoamingNetwork.DCMeterSerial, token, (begin.AddHours(-2),   "T", 990.000m)))
            ));

            var (status, finished) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            var values = finished["check"]?["meterValues"];

            using (Assert.EnterMultipleScope())
            {
                Assert.That(values?["verified"]?.Value<Boolean>(),                  Is.False, finished.ToString());
                Assert.That(values?["values"]?[0]?["verified"]?.Value<Boolean>(),   Is.True,  "the begin is the meter's");
                Assert.That(values?["values"]?[1]?["verified"]?.Value<Boolean>(),   Is.False);
                Assert.That(values?["values"]?[1]?["result"]?.Value<String>(),      Is.EqualTo("InvalidSignature"));
                Assert.That(values?["values"]?[2]?["error"]?.Value<String>(),       Does.Contain("No key of the meter AC-2026-001"));
                Assert.That(values?["values"]?[2]?["result"]?.Type,                 Is.EqualTo(JTokenType.Null), "a value of another meter is checked with no key");
                Assert.That(values?["values"]?[3]?["error"]?.Value<String>(),       Does.Contain("No key of the meter " + TestRoamingNetwork.DCMeterSerial));
                Assert.That(values?["energyKWh"]?.Type,                             Is.EqualTo(JTokenType.Null), "no end the meter signed");
                Assert.That(values?["energyMatches"]?.Type,                         Is.EqualTo(JTokenType.Null));
            }

        }

        #endregion

        #region AKeyNoLongerTrustedChecksNothingOfASession()

        /// <summary>
        /// A session is checked under the identity keys trusted now: once an
        /// administrator no longer trusts the keys that attested its station,
        /// nothing of it is checked - its meter values no more than its price.
        /// </summary>
        [Test]
        public async Task AKeyNoLongerTrustedChecksNothingOfASession()
        {

            var (admin, driver, session, token) = await ChargingThroughTheProvider();

            using var _admin   = admin;
            using var _driver  = driver;

            var id = session["id"]!.Value<String>()!;

            emsp!.OperatorCDR(token, 12.5m, 8.13m, DateTimeOffset.UtcNow, SignedValues(
                ("End", TestRoamingNetwork.OCMF(network!.DCMeter, TestRoamingNetwork.DCMeterSerial, token, (DateTimeOffset.UtcNow, "E", 1012.500m)))
            ));

            var (_, before) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            foreach (var keyId in network.TrustAnchor.Keys)
            {
                var (status, withdrawn) = await Send(admin, HttpMethod.Delete, $"api/v1/configuration/roamingNetwork/trustedKeys/{keyId}");
                Assert.That(status, Is.EqualTo(HttpStatusCode.OK), withdrawn.ToString());
            }

            var (_, after) = await Send(driver, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(before["check"]?["meterValues"]?["verified"]?.Value<Boolean>(), Is.True, before.ToString());
                Assert.That(after ["check"]?["error"]?.Value<String>(),                     Does.StartWith("Nothing of it is checked"), after.ToString());
                Assert.That(after ["check"]?["meterValues"],                                Is.Null);
                Assert.That(after ["check"]?["price"],                                      Is.Null);
            }

        }

        #endregion


        #region (helpers) MapWhen(Client, Condition, What) / EVSEStatusOf(Map, EVSEId) / StatusLive(Map)

        /// <summary>
        /// The map, asked again until it is as the condition says - for at most
        /// fifteen seconds, and failed with what it was last otherwise.
        /// </summary>
        private static async Task<JObject> MapWhen(HttpClient           Client,
                                                   Func<JObject, Boolean>  Condition,
                                                   String               What)
        {

            var until  = DateTimeOffset.UtcNow.AddSeconds(15);
            var map    = new JObject();

            do
            {

                (_, map) = await Send(Client, HttpMethod.Get, "api/v1/map");

                if (Condition(map))
                    return map;

                await Task.Delay(100);

            }
            while (DateTimeOffset.UtcNow < until);

            Assert.Fail($"{What}: {map}");

            return map;

        }

        private static String? EVSEStatusOf(JObject Map, String EVSEId)

            => Map["locations"]?.SelectMany(location => location["evses"] ?? new JArray()).
                                 FirstOrDefault(evse => evse["evse_id"]?.Value<String>() == EVSEId)?["status"]?.Value<String>();

        private static Boolean StatusLive(JObject Map)

            => Map["roamingNetwork"]?["status"]?["live"]?.Value<Boolean>() == true;

        #endregion

        #region TheMapShowsWhichChargePointIsFreeAsTheNetworkSaysIt()

        /// <summary>
        /// The status of the charge points of the roaming network, followed over
        /// its WebSocket: on the map as the network says it, pushed as it
        /// changes - and no start where it says a charge point is taken, or out
        /// of service, until it is free again.
        /// </summary>
        [Test]
        public async Task TheMapShowsWhichChargePointIsFreeAsTheNetworkSaysIt()
        {

            using var admin   = await WithTheRoamingNetwork();
            using var driver  = await SignedInAs("driver1", "driver");

            var map     = await MapWhen(driver, StatusLive, "the status of the roaming network is not followed");
            var status  = map["roamingNetwork"]!["status"]!;

            using (Assert.EnterMultipleScope())
            {
                Assert.That(status["from"]!.Value<String>(),                    Is.EqualTo(Roaming.RoamingNetworkStatus.WebSocketURLOf(network!.URL)));
                Assert.That(status["since"]!.Type,                              Is.EqualTo(JTokenType.Date));
                Assert.That(status["error"]!.Type,                              Is.EqualTo(JTokenType.Null));
                Assert.That(status["evses"]!.Value<Int32>(),                    Is.EqualTo(2));
                Assert.That(EVSEStatusOf(map, TestRoamingNetwork.ACEVSE),       Is.EqualTo("AVAILABLE"));
                Assert.That(EVSEStatusOf(map, TestRoamingNetwork.DCEVSE),       Is.EqualTo("AVAILABLE"));
            }

            // Somebody else charges at the AC point, and the DC point is taken
            // out of service by its operator.
            var now = DateTimeOffset.UtcNow;

            Assert.That(network.History.RuntimeStatus.TryUpdate(POIRuntimeStatusKind.Status,      "EVSE", TestRoamingNetwork.ACEVSE, "charging",     now, out _, out var error), Is.EqualTo(POIStatusUpdateResult.Updated), error);
            Assert.That(network.History.RuntimeStatus.TryUpdate(POIRuntimeStatusKind.AdminStatus, "EVSE", TestRoamingNetwork.DCEVSE, "outOfService", now, out _, out error),     Is.EqualTo(POIStatusUpdateResult.Updated), error);

            await MapWhen(driver,
                          map => EVSEStatusOf(map, TestRoamingNetwork.ACEVSE) == "CHARGING" && EVSEStatusOf(map, TestRoamingNetwork.DCEVSE) == "INOPERATIVE",
                          "the status the network pushed is not on the map");

            var (code, check) = await Send(driver, HttpMethod.Get, $"api/v1/map/locations/{TestRoamingNetwork.PoolId}/check");

            Assert.That(check["verified"]!.Value<Boolean>(), Is.True, check.ToString());

            foreach (var (evse, why) in new[] { (TestRoamingNetwork.ACEVSE, "is charging, not available"), (TestRoamingNetwork.DCEVSE, "is inoperative, not available") })
            {

                (code, var refused) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = TestRoamingNetwork.PoolId, evseUid = evse });

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(code,                               Is.EqualTo(HttpStatusCode.Conflict), refused.ToString());
                    Assert.That(refused["error"]!.Value<String>(),  Does.Contain(why));
                }

            }

            // Free again: started at.
            Assert.That(network.History.RuntimeStatus.TryUpdate(POIRuntimeStatusKind.Status, "EVSE", TestRoamingNetwork.ACEVSE, "available", DateTimeOffset.UtcNow, out _, out error), Is.EqualTo(POIStatusUpdateResult.Updated), error);

            await MapWhen(driver, map => EVSEStatusOf(map, TestRoamingNetwork.ACEVSE) == "AVAILABLE", "the AC point is not free again on the map");

            (code, var session) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = TestRoamingNetwork.PoolId, evseUid = TestRoamingNetwork.ACEVSE });

            Assert.That(code, Is.EqualTo(HttpStatusCode.Created), session.ToString());

        }

        #endregion

        #region TheStatusIsFollowedAtAMirrorAndNotKnownOnceNoneAnswers()

        /// <summary>
        /// A trust anchor naming a URL nobody answers at first and the network's
        /// own second: the status is followed at the second. Once the network
        /// is gone too, no status is known - none is kept from a connection
        /// that ended - and why is said; a charge point is unknown again, and
        /// started at as one a driver stands in front of.
        /// </summary>
        [Test]
        public async Task TheStatusIsFollowedAtAMirrorAndNotKnownOnceNoneAnswers()
        {

            network = await TestRoamingNetwork.Start();

            var down    = URL.Parse($"http://127.0.0.1:{TestPorts.Free()}/RNs/QA");
            var anchor  = new RoamingNetworkTrustAnchor(network.TrustAnchor.Id, [ down, network.TrustAnchor.URLs[0] ], network.TrustAnchor.Keys);

            using var admin = await WithTheRoamingNetwork(anchor);

            var map = await MapWhen(admin, StatusLive, "the status was not followed at the mirror");

            Assert.That(map["roamingNetwork"]!["status"]!["from"]!.Value<String>(), Is.EqualTo(Roaming.RoamingNetworkStatus.WebSocketURLOf(network.URL)));

            await network.DisposeAsync();
            network = null;

            map = await MapWhen(admin, map => !StatusLive(map), "the status is still said to be known, of a network that is gone");

            using (Assert.EnterMultipleScope())
            {
                Assert.That(map["roamingNetwork"]!["status"]!["error"]!.Value<String>(),  Is.Not.Empty);
                Assert.That(map["roamingNetwork"]!["status"]!["from"]!.Type,              Is.EqualTo(JTokenType.Null));
                Assert.That(EVSEStatusOf(map, TestRoamingNetwork.ACEVSE),                 Is.EqualTo("UNKNOWN"));
            }

        }

        #endregion

        #region AStatusIsSaidInOCPIsWords()

        /// <summary>
        /// The admin status and the status of an EVSE in POI's words, as the map
        /// says them in OCPI's: what its operator declares first, then what it
        /// is - and the WebSocket of a history below a URL of a trust anchor.
        /// </summary>
        [Test]
        public void AStatusIsSaidInOCPIsWords()
        {

            using (Assert.EnterMultipleScope())
            {
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf(null,            "available"),         Is.EqualTo("AVAILABLE"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("operational",   "waitingForPlugin"),  Is.EqualTo("AVAILABLE"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("Operational",   "Charging"),          Is.EqualTo("CHARGING"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("operational",   "occupied"),          Is.EqualTo("CHARGING"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("operational",   "reserved"),          Is.EqualTo("RESERVED"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("outOfService",  "available"),         Is.EqualTo("INOPERATIVE"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("internalUse",   "available"),         Is.EqualTo("BLOCKED"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("planned",       null),                Is.EqualTo("PLANNED"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("operational",   "error"),             Is.EqualTo("OUTOFORDER"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("operational",   "offline"),           Is.EqualTo("OUTOFORDER"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("operational",   "removed"),           Is.EqualTo("REMOVED"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("operational",   "somethingNew"),      Is.EqualTo("UNKNOWN"));
                Assert.That(Roaming.RoamingNetworkStatus.OCPIStatusOf("operational",   null),                Is.EqualTo("UNKNOWN"));

                Assert.That(Roaming.RoamingNetworkStatus.WebSocketURLOf("https://hub.example/poi/RNs/QA/"),  Is.EqualTo("wss://hub.example/poi/RNs/QA/history/ws"));
                Assert.That(Roaming.RoamingNetworkStatus.WebSocketURLOf("http://127.0.0.1:2356/RNs/QA"),     Is.EqualTo("ws://127.0.0.1:2356/RNs/QA/history/ws"));
            }

        }

        #endregion

        #region AWebPaymentPageIsTheMostSpecificAndOnlyHTTPS()

        /// <summary>
        /// The web payment page of a station as its proof gives it: its own
        /// before that of its pool before that of its operator - and only an
        /// https URL linked, the first one given deciding, so that a station's
        /// own page over http is not passed over for its operator's.
        /// </summary>
        [Test]
        public void AWebPaymentPageIsTheMostSpecificAndOnlyHTTPS()
        {

            static JObject With(String? URL)
                => URL is null ? [] : new JObject(new JProperty("webPaymentURL", URL));

            var ofStation   = Roaming.StationCheck.WebPaymentJSON(With("https://pay.example/S1"), With("https://pay.example/P1"), With("https://pay.example/"));
            var ofPool      = Roaming.StationCheck.WebPaymentJSON(With(null),                     With("https://pay.example/P1"), With("https://pay.example/"));
            var ofOperator  = Roaming.StationCheck.WebPaymentJSON(With(null),                     With(null),                     With("https://pay.example/"));
            var none        = Roaming.StationCheck.WebPaymentJSON(With(null),                     With(null),                     With(null));
            var plain       = Roaming.StationCheck.WebPaymentJSON(With("http://pay.example/S1"),  With(null),                     With("https://pay.example/"));
            var script      = Roaming.StationCheck.WebPaymentJSON(With("javascript:alert(1)"),    With(null),                     With(null));

            using (Assert.EnterMultipleScope())
            {
                Assert.That(ofStation! ["url"]!.Value<String>(),  Is.EqualTo("https://pay.example/S1"));
                Assert.That(ofStation  ["of"]!. Value<String>(),  Is.EqualTo("chargingStation"));
                Assert.That(ofPool!    ["url"]!.Value<String>(),  Is.EqualTo("https://pay.example/P1"));
                Assert.That(ofPool     ["of"]!. Value<String>(),  Is.EqualTo("chargingPool"));
                Assert.That(ofOperator!["of"]!. Value<String>(),  Is.EqualTo("chargingStationOperator"));
                Assert.That(none,                                 Is.Null);

                Assert.That(plain!     ["url"]!.Type,             Is.EqualTo(JTokenType.Null));
                Assert.That(plain      ["of"]!. Value<String>(),  Is.EqualTo("chargingStation"));
                Assert.That(plain      ["error"]!.Value<String>(), Does.Contain("is no https URL"));
                Assert.That(script!    ["url"]!.Type,             Is.EqualTo(JTokenType.Null));
            }

        }

        #endregion

    }

}
