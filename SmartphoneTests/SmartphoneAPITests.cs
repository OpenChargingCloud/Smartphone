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

using cloud.charging.open.Smartphone.Map;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// The JSON API of a smartphone below "v1/map", "v1/configuration/map"
    /// and "v1/charging", against a started smartphone and over HTTP: what an
    /// administrator, a driver and a viewer may each do there, and what the
    /// answers say - and the security headers its pages are served with.
    /// </summary>
    public class SmartphoneAPITests
    {

        #region Data

        private const String  NoTimeServers  = """{ "nts": { "enabled": false } }""";

        private String       directory  = "";
        private Smartphone?  smartphone;
        private Uri?         address;

        #endregion

        #region Setup / TearDown

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "smartphone-api-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public async Task TearDown()
        {

            if (smartphone is not null)
                await smartphone.DisposeAsync();

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

            var account = await smartphone.ExtAPI.CreateUser(
                                    User_Id.Parse(Name),
                                    I18NString.Create(Languages.en, Name),
                                    SimpleEMailAddress.Parse($"{Name}@localhost"),
                                    User2OrganizationEdgeLabel.IsMember,
                                    (Organization) organization!,
                                    Password:                  password,
                                    SkipDefaultNotifications:  true,
                                    SkipNewUserEMail:          true,
                                    SkipNewUserNotifications:  true,
                                    AcceptedEULA:              DateTimeOffset.UtcNow.AddSeconds(-1),
                                    IsAuthenticated:           true
                                );

            Assert.That(account, Is.Not.Null);
            Assert.That(smartphone.ExtAPI.TryGetUser(User_Id.Parse(Name), out var stored), Is.True);
            Assert.That(smartphone.ExtAPI.TryGetUserGroup(UserGroup_Id.Parse(Role), out var group), Is.True, $"the smartphone has no group '{Role}'");
            Assert.That((await smartphone.ExtAPI.AddUserToUserGroup((User) stored!, User2UserGroupEdgeLabel.IsMember, (UserGroup) group!)).IsSuccess, Is.True);

            var client = new HttpClient { BaseAddress = address, Timeout = TimeSpan.FromSeconds(30) };

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
        /// A demo station with a point that is free now, and stays so for the
        /// next minute - so that a test does not start on the edge of the ten
        /// minutes in which the demo's points come and go.
        /// </summary>
        private static (ChargingLocation Location, ChargingEVSE EVSE) AFreePoint()
        {

            var now  = DateTimeOffset.UtcNow;

            return DemoLocations.All.
                       SelectMany(location => location.EVSEs.Select(evse => (location, evse))).
                       First(pair => DemoLocations.StatusOf(pair.evse, now)                         == EVSEStatus.Available &&
                                     DemoLocations.StatusOf(pair.evse, now + TimeSpan.FromMinutes(1)) == EVSEStatus.Available);

        }

        #endregion


        #region ADriverChargesAndAViewerOnlyLooks()

        /// <summary>
        /// A driver sees the map, starts a session at a free point - which the
        /// map then shows as charging - finds it among the sessions and stops
        /// it; a second start at the same point is refused while the first
        /// charges, and so is a point out of order. A viewer sees all of it and
        /// starts nothing.
        /// </summary>
        [Test]
        public async Task ADriverChargesAndAViewerOnlyLooks()
        {

            await TestPorts.StartedOnFreshPorts(SmartphoneFrom);

            using var driver  = await SignedInAs("driver1", "driver");
            using var viewer  = await SignedInAs("viewer1", "viewer");

            var (location, evse) = AFreePoint();

            #region The map

            var (status, map) = await Send(driver, HttpMethod.Get, "api/v1/map");

            Assert.That(status,                                     Is.EqualTo(HttpStatusCode.OK), map.ToString());
            Assert.That(map["source"]!.Value<String>(),             Is.EqualTo("demo"));
            Assert.That(map["locations"]!.Count(),                  Is.EqualTo(DemoLocations.All.Count));
            Assert.That(map["tiles"]!.Value<String>(),              Does.StartWith(Smartphone.MapTileServer));

            #endregion

            #region A viewer starts nothing

            (status, var refused) = await Send(viewer, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = location.Id, evseUid = evse.Uid });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), refused.ToString());

            #endregion

            #region A driver starts one ...

            (status, var started) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = location.Id, evseUid = evse.Uid });

            Assert.That(status,                                      Is.EqualTo(HttpStatusCode.Created), started.ToString());
            Assert.That(started["status"]!.Value<String>(),          Is.EqualTo("charging"));
            Assert.That(started["startedBy"]!.Value<String>(),       Is.EqualTo("driver1"));
            Assert.That(started["evse"]!["evseId"]!.Value<String>(), Is.EqualTo(evse.EVSEId));

            var id = started["id"]!.Value<String>()!;

            (status, map) = await Send(driver, HttpMethod.Get, "api/v1/map");

            var shown = map["locations"]!.First(one => one["id"]!.Value<String>() == location.Id)!["evses"]!.First(one => one["uid"]!.Value<String>() == evse.Uid)!;

            Assert.That(shown["status"]!.Value<String>(), Is.EqualTo(EVSEStatus.Charging), "the map does not show the point this phone charges at as charging");

            #endregion

            #region ... and not twice, nor at a point out of order, nor where there is none

            (status, var twice) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = location.Id, evseUid = evse.Uid });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict), twice.ToString());

            (status, var broken) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = "DEMO-JENA-BEUTENBERG", evseUid = "2" });

            Assert.That(status,                               Is.EqualTo(HttpStatusCode.Conflict), broken.ToString());
            Assert.That(broken["error"]!.Value<String>(),     Does.Contain("out of order"));

            (status, var nowhere) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = "NOWHERE", evseUid = "1" });

            Assert.That(status, Is.EqualTo(HttpStatusCode.NotFound), nowhere.ToString());

            (status, var unsaid) = await Send(driver, HttpMethod.Post, "api/v1/charging/sessions", new { locationId = location.Id });

            Assert.That(status, Is.EqualTo(HttpStatusCode.BadRequest), unsaid.ToString());

            #endregion

            #region The sessions, as the viewer sees them

            (status, var active) = await Send(viewer, HttpMethod.Get, "api/v1/charging/sessions?active=true");

            Assert.That(status,                                                       Is.EqualTo(HttpStatusCode.OK), active.ToString());
            Assert.That(active["sessions"]!.Select(one => one["id"]!.Value<String>()), Is.EqualTo(new[] { id }));
            Assert.That(active["simulated"]!.Value<Boolean>(),                        Is.True);

            (status, var one) = await Send(viewer, HttpMethod.Get, $"api/v1/charging/sessions/{id}");

            Assert.That(status,                                Is.EqualTo(HttpStatusCode.OK), one.ToString());
            Assert.That(one["location"]!["name"]!.Value<String>(), Is.EqualTo(location.Name));

            (status, _) = await Send(viewer, HttpMethod.Post, $"api/v1/charging/sessions/{id}/stop");

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden));

            #endregion

            #region The driver stops it, once

            (status, var stopped) = await Send(driver, HttpMethod.Post, $"api/v1/charging/sessions/{id}/stop");

            Assert.That(status,                                Is.EqualTo(HttpStatusCode.OK), stopped.ToString());
            Assert.That(stopped["status"]!.Value<String>(),    Is.EqualTo("stopped"));
            Assert.That(stopped["stoppedBy"]!.Value<String>(), Is.EqualTo("driver1"));

            (status, var again) = await Send(driver, HttpMethod.Post, $"api/v1/charging/sessions/{id}/stop");

            Assert.That(status, Is.EqualTo(HttpStatusCode.Conflict), again.ToString());

            (status, var gone) = await Send(driver, HttpMethod.Get, "api/v1/charging/sessions/NOSUCHONE");

            Assert.That(status, Is.EqualTo(HttpStatusCode.NotFound), gone.ToString());

            (status, var all) = await Send(driver, HttpMethod.Get, "api/v1/charging/sessions");

            Assert.That(all["total"]!.Value<Int32>(),  Is.EqualTo(1));
            Assert.That(all["active"]!.Value<Int32>(), Is.EqualTo(0));

            Assert.That(smartphone!.Log.Recent(500).Select(entry => entry.Message),
                        Has.Some.Contains("'driver1' started charging at").And.Contains("simulated"));

            #endregion

        }

        #endregion

        #region TheMapsSourceIsTheAdministratorsToChange()

        /// <summary>
        /// Where the map takes its stations from is empty at first, changed by
        /// an administrator - into the configuration file - and refused to a
        /// driver and to a URL that is none.
        /// </summary>
        [Test]
        public async Task TheMapsSourceIsTheAdministratorsToChange()
        {

            await TestPorts.StartedOnFreshPorts(SmartphoneFrom);

            using var admin   = await SignedInAs("admin1",  "systemadmin");
            using var driver  = await SignedInAs("driver1", "driver");

            var (status, settings) = await Send(driver, HttpMethod.Get, "api/v1/configuration/map");

            Assert.That(status,                                    Is.EqualTo(HttpStatusCode.OK), settings.ToString());
            Assert.That(settings["stationsURL"]!.Value<String>(),  Is.Empty);

            (status, var refused) = await Send(driver, HttpMethod.Put, "api/v1/configuration/map", new { stationsURL = "https://emsp.example.org/locations" });

            Assert.That(status, Is.EqualTo(HttpStatusCode.Forbidden), refused.ToString());

            (status, var wrong) = await Send(admin, HttpMethod.Put, "api/v1/configuration/map", new { stationsURL = "not a URL" });

            Assert.That(status,                             Is.EqualTo(HttpStatusCode.BadRequest), wrong.ToString());
            Assert.That(wrong["error"]!.Value<String>(),    Does.Contain("no absolute URL"));

            (status, settings) = await Send(admin, HttpMethod.Put, "api/v1/configuration/map", new { stationsURL = "https://emsp.example.org/locations" });

            Assert.That(status,                                    Is.EqualTo(HttpStatusCode.OK), settings.ToString());
            Assert.That(settings["stationsURL"]!.Value<String>(),  Is.EqualTo("https://emsp.example.org/locations"));

            var file = JObject.Parse(File.ReadAllText(Path.Combine(directory, WWCPConfigFile.DefaultFileName)));

            Assert.That(file["map"]?["stationsURL"]?.Value<String>(), Is.EqualTo("https://emsp.example.org/locations"), "it was not written into the configuration file");
            Assert.That(file["nts"]?["enabled"]?.Value<Boolean>(),    Is.False, "the rest of the configuration file was not kept");

            (status, var map) = await Send(driver, HttpMethod.Get, "api/v1/map");

            Assert.That(map["stationsURL"]!.Value<String>(),  Is.EqualTo("https://emsp.example.org/locations"));
            Assert.That(map["source"]!.Value<String>(),       Is.EqualTo("demo"), "the URL is asked already, which nothing asks yet");

        }

        #endregion

        #region ItsPagesMayLoadTheTilesAndAskWhereTheyAre()

        /// <summary>
        /// The pages of the web interface may load the map's tiles from
        /// OpenStreetMap and ask the browser where it is - and nothing else
        /// they might not before: scripts and styles are this smartphone's
        /// alone, as on every node.
        /// </summary>
        [Test]
        public async Task ItsPagesMayLoadTheTilesAndAskWhereTheyAre()
        {

            await TestPorts.StartedOnFreshPorts(SmartphoneFrom);

            using var client   = new HttpClient { BaseAddress = address };
            using var response = await client.GetAsync("map");

            var policy      = response.Headers.TryGetValues("Content-Security-Policy", out var policies)    ? String.Join(", ", policies)    : "";
            var permissions = response.Headers.TryGetValues("Permissions-Policy",      out var permitted)   ? String.Join(", ", permitted)   : "";

            if (response.StatusCode == HttpStatusCode.NotFound)
                Assert.Ignore("This build has no web interface - built with SkipFrontendBuild and nothing in dist/.");

            Assert.Multiple(() => {
                Assert.That(policy,       Does.Contain($"img-src 'self' data: {Smartphone.MapTileServer};"));
                Assert.That(policy,       Does.Contain("script-src 'self';"));
                Assert.That(policy,       Does.Contain("style-src 'self';"));
                Assert.That(policy,       Does.Contain("connect-src 'self';"));
                Assert.That(permissions,  Does.Contain("geolocation=(self)"));
                Assert.That(permissions,  Does.Contain("camera=()"));
            });

        }

        #endregion

    }

}
