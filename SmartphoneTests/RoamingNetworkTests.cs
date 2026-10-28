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

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// The map of a smartphone given the trust anchor of a roaming network,
    /// against a roaming network served on the loopback: its charging pools on
    /// the map, the proof of each station checked against the trust anchor -
    /// and refused where nobody it trusts attested it - and everything a proof
    /// that holds gives, its tariffs, its price statements and the public keys
    /// its energy meters sign their measurements with.
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

            // One instance runs every test of the fixture: a network of the
            // test before, disposed, is none of the next.
            smartphone  = null;
            network     = null;
            other       = null;
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

        /// <summary>
        /// A smartphone whose time client is switched off, on a free port of
        /// the loopback - made, and not yet started.
        /// </summary>
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
        /// A started smartphone, the roaming network of the tests, and an
        /// administrator who gave the smartphone its trust anchor - as the text
        /// of its QR code.
        /// </summary>
        private async Task<HttpClient> WithTheRoamingNetwork(RoamingNetworkTrustAnchor? TrustAnchor = null)
        {

            network ??= await TestRoamingNetwork.Start();

            await TestPorts.StartedOnFreshPorts(SmartphoneFrom);

            var admin = await SignedInAs("admin1", "systemadmin");

            var (status, settings) = await Send(admin, HttpMethod.Put, "api/v1/configuration/roamingNetwork",
                                                new { trustAnchor = (TrustAnchor ?? network.TrustAnchor).ToQRCode() });

            Assert.That(status, Is.EqualTo(HttpStatusCode.OK), settings.ToString());

            return admin;

        }

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

            await TestPorts.StartedOnFreshPorts(SmartphoneFrom);

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
                Assert.That(evses.Select(evse => evse["status"]!.Value<String>()),      Is.All.EqualTo("UNKNOWN"));
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

                Assert.That(dcEVSE["id"]!.Value<String>(),                                       Is.EqualTo(TestRoamingNetwork.DCEVSE));
                Assert.That(dcEVSE["maxPowerKW"]!.Value<Decimal>(),                              Is.EqualTo(150m));
                Assert.That(dcEVSE["tariffs"]![0]!["id"]!.Value<String>(),                       Is.EqualTo(TestRoamingNetwork.DCTariff));

                // The tariff says 0.69, the statement of the EVSE 0.65: the
                // statement wins. The AC EVSE has none, and keeps its tariff.
                Assert.That(dcEVSE["price"]!["pricePerKWh"]!.Value<Decimal>(),                   Is.EqualTo(0.65m));
                Assert.That(acEVSE["price"]!["pricePerKWh"]!.Value<Decimal>(),                   Is.EqualTo(0.49m));
                Assert.That(acEVSE["price"]!["currency"]!.Value<String>(),                       Is.EqualTo("EUR"));

                Assert.That(dc["priceStatements"]!.Single()["verified"]!.Value<Boolean>(),       Is.True, dc["priceStatements"]!.ToString());
                Assert.That(dc["priceStatements"]!.Single()["signer"]!.Value<String>(),          Is.EqualTo("DE*GEF"));

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

    }

}
