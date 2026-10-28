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

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using cloud.charging.open.Smartphone.Charging;
using cloud.charging.open.Smartphone.Map;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// A clock that says what it is told: the time a test wants, and later
    /// the time it moves it on to.
    /// </summary>
    internal sealed class SetClock(DateTimeOffset Now) : TimeProvider
    {

        public DateTimeOffset Now { get; set; } = Now;

        public override DateTimeOffset GetUtcNow() => Now;

        public void Advance(TimeSpan By) => Now += By;

    }


    /// <summary>
    /// Where the map takes its stations from, which stations it has, and the
    /// simulated sessions charged at them: what each charges by when, what
    /// stops one, and what of them a restart keeps.
    /// </summary>
    public class ChargingTests
    {

        #region Data

        private static readonly DateTimeOffset  Monday     = new (2026, 10, 12, 8, 0, 0, TimeSpan.Zero);

        private static readonly ChargingLocation Holzmarkt  = DemoLocations.Location("DEMO-JENA-HOLZMARKT")!;
        private static readonly ChargingLocation Lobeda     = DemoLocations.Location("DEMO-JENA-LOBEDA")!;

        private String directory = "";

        #endregion

        #region Setup / TearDown

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "smartphone-charging-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public void TearDown()
        {
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

        /// <summary>Every EVSE available, whatever the demo says.</summary>
        private static String AllFree(ChargingEVSE EVSE, DateTimeOffset Now) => EVSEStatus.Available;

        private ChargingSessions SessionsIn(SetClock Clock)
            => new (Path.Combine(directory, ChargingSessions.DefaultFileName), Clock);

        #endregion


        #region TheMapHasNoSourceUntilItIsGivenOne()

        /// <summary>
        /// No "map" section is no data source; an empty URL is none either;
        /// an absolute http or https URL is one; anything else is refused with
        /// why.
        /// </summary>
        [Test]
        public void TheMapHasNoSourceUntilItIsGivenOne()
        {

            Assert.Multiple(() => {

                Assert.That(MapConfiguration.FromDocument(new JObject(), out var none, out _),                                      Is.True);
                Assert.That(none!.StationsURL,                                                                                     Is.Empty);

                Assert.That(MapConfiguration.TryParse(JObject.Parse("""{ "stationsURL": "" }"""), out var empty, out _),           Is.True);
                Assert.That(empty!.StationsURL,                                                                                    Is.Empty);

                Assert.That(MapConfiguration.TryParse(JObject.Parse("""{ "stationsURL": " https://emsp.example.org/locations " }"""), out var given, out _), Is.True);
                Assert.That(given!.StationsURL,                                                                                    Is.EqualTo("https://emsp.example.org/locations"));
                Assert.That(given.ToJSON()["stationsURL"]!.Value<String>(),                                                        Is.EqualTo("https://emsp.example.org/locations"));

                Assert.That(MapConfiguration.TryParse(JObject.Parse("""{ "stationsURL": "emsp.example.org" }"""),  out _, out var relative), Is.False);
                Assert.That(relative,                                                                                              Does.Contain("no absolute URL"));

                Assert.That(MapConfiguration.TryParse(JObject.Parse("""{ "stationsURL": "ftp://emsp.example.org" }"""), out _, out var ftp), Is.False);
                Assert.That(ftp,                                                                                                   Does.Contain("no http or https URL"));

                Assert.That(MapConfiguration.TryParse(JObject.Parse("""{ "stationsURL": 42 }"""),                  out _, out var number), Is.False);
                Assert.That(number,                                                                                                Does.Contain("in quotes"));

                Assert.That(MapConfiguration.FromDocument(JObject.Parse("""{ "map": [] }"""),                      out _, out var array), Is.False);
                Assert.That(array,                                                                                                 Does.Contain("no JSON object"));

            });

        }

        #endregion

        #region TheDemoStationsAreWhereTheySayAndComeAndGo()

        /// <summary>
        /// Every demo station is somewhere on the earth near Jena, has an id
        /// of its own and EVSE IDs of their own - and what its EVSEs are stays
        /// for ten minutes and changes after, except where it stays for good.
        /// </summary>
        [Test]
        public void TheDemoStationsAreWhereTheySayAndComeAndGo()
        {

            var evses = DemoLocations.All.SelectMany(location => location.EVSEs).ToArray();

            var statusesOverADay = Enumerable.Range(0, 144).
                                       Select(slot => Monday + slot * DemoLocations.Slot).
                                       SelectMany(time => evses.Select(evse => DemoLocations.StatusOf(evse, time))).
                                       ToHashSet();

            var oneEVSE  = Holzmarkt.EVSEs[0];
            var slotTimes = Enumerable.Range(0, 144).Select(slot => DemoLocations.StatusOf(oneEVSE, Monday + slot * DemoLocations.Slot)).ToHashSet();

            Assert.Multiple(() => {

                Assert.That(DemoLocations.All.Select(location => location.Id),   Is.Unique);
                Assert.That(evses.Select(evse => evse.EVSEId),                   Is.Unique);

                foreach (var location in DemoLocations.All)
                {
                    Assert.That(location.Latitude,   Is.InRange(50.8, 51.1),   location.Name);
                    Assert.That(location.Longitude,  Is.InRange(10.9, 11.7),   location.Name);
                    Assert.That(location.EVSEs,      Is.Not.Empty,             location.Name);
                }

                Assert.That(DemoLocations.StatusOf(oneEVSE, Monday),
                            Is.EqualTo(DemoLocations.StatusOf(oneEVSE, Monday + TimeSpan.FromMinutes(9))),
                            "within its ten minutes an EVSE changed");

                Assert.That(slotTimes,         Is.EquivalentTo(new[] { EVSEStatus.Available, EVSEStatus.Charging }), "over a day one EVSE never changed");
                Assert.That(statusesOverADay,  Does.Contain(EVSEStatus.OutOfOrder).And.Contain(EVSEStatus.Planned).And.Contain(EVSEStatus.Reserved));

                Assert.That(DemoLocations.StatusOf(DemoLocations.Location("DEMO-JENA-NORD")!.EVSEs[0], Monday), Is.EqualTo(EVSEStatus.Planned));

            });

        }

        #endregion

        #region ASessionChargesWithWhatBothSidesTakeUntilFull()

        /// <summary>
        /// At a 22 kW AC point the simulated vehicle takes 11 kW, at a 300 kW
        /// DC point 150 kW - and either charges at that until 30 kWh are in,
        /// and is finished then.
        /// </summary>
        [Test]
        public void ASessionChargesWithWhatBothSidesTakeUntilFull()
        {

            var ac  = ChargingSession.New("AC", Holzmarkt, Holzmarkt.EVSEs[0], Monday, "root");
            var dc  = ChargingSession.New("DC", Lobeda,    Lobeda.EVSEs[0],    Monday, "root");

            Assert.Multiple(() => {

                Assert.That(ac.PowerKW,                                          Is.EqualTo(11m));
                Assert.That(dc.PowerKW,                                          Is.EqualTo(150m));

                Assert.That(ac.EnergyAt(Monday + TimeSpan.FromMinutes(30)),      Is.EqualTo(5.5m));
                Assert.That(ac.StatusAt(Monday + TimeSpan.FromMinutes(30)),      Is.EqualTo(ChargingSessionStatus.Charging));

                Assert.That(dc.FullAt,                                           Is.EqualTo(Monday + TimeSpan.FromMinutes(12)));
                Assert.That(dc.StatusAt(Monday + TimeSpan.FromMinutes(12)),      Is.EqualTo(ChargingSessionStatus.Finished));
                Assert.That(dc.EnergyAt(Monday + TimeSpan.FromHours(5)),         Is.EqualTo(30m), "it charged on after it was full");

                var json = dc.ToJSON(Monday + TimeSpan.FromMinutes(6));

                Assert.That(json["status"]!.Value<String>(),                     Is.EqualTo("charging"));
                Assert.That(json["energyKWh"]!.Value<Decimal>(),                 Is.EqualTo(15m));
                Assert.That(json["progressPercent"]!.Value<Decimal>(),           Is.EqualTo(50m));
                Assert.That(json["cost"]!["amount"]!.Value<Decimal>(),           Is.EqualTo(11.85m));
                Assert.That(json["durationSeconds"]!.Value<Int64>(),             Is.EqualTo(360));
                Assert.That(json["simulated"]!.Value<Boolean>(),                 Is.True);

                var over = dc.ToJSON(Monday + TimeSpan.FromHours(1));

                Assert.That(over["status"]!.Value<String>(),                     Is.EqualTo("finished"));
                Assert.That(over["powerKW"]!.Value<Decimal>(),                   Is.EqualTo(0m));
                Assert.That(over["durationSeconds"]!.Value<Int64>(),             Is.EqualTo(720), "a finished session went on counting its time");

            });

        }

        #endregion

        #region ASessionStartsWhereAPointIsFreeAndStopsOnce()

        /// <summary>
        /// A session starts at a free point and at no other, not twice at the
        /// same one, and stops once: what it charged until then is what it
        /// charged.
        /// </summary>
        [Test]
        public void ASessionStartsWhereAPointIsFreeAndStopsOnce()
        {

            var clock     = new SetClock(Monday);
            var sessions  = SessionsIn(clock);

            Assert.That(sessions.TryStart(Holzmarkt, Holzmarkt.EVSEs[0], AllFree, "root", null, out var started, out var error, out _), Is.True, error);

            var again     = sessions.TryStart(Holzmarkt, Holzmarkt.EVSEs[0], AllFree, "root", null, out _, out var twice, out _);
            var busy      = sessions.TryStart(Lobeda, Lobeda.EVSEs[0], (_, _) => EVSEStatus.Charging, "root", null, out _, out var notFree, out _);
            var parallel  = sessions.TryStart(Lobeda, Lobeda.EVSEs[1], AllFree, "root", null, out var second, out _, out _);

            clock.Advance(TimeSpan.FromMinutes(30));

            var stopped   = sessions.TryStop(started!.Id, "root", out var stoppedSession, out _, out _, out _);

            clock.Advance(TimeSpan.FromMinutes(30));

            var stopAgain = sessions.TryStop(started.Id, "root", out _, out var over, out var notFound, out _);
            var noSuch    = sessions.TryStop("NOSUCHONE", "root", out _, out _, out var noSuchNotFound, out _);

            Assert.Multiple(() => {

                Assert.That(again,                                            Is.False);
                Assert.That(twice,                                            Does.Contain("already charging"));
                Assert.That(busy,                                             Is.False);
                Assert.That(notFree,                                          Does.Contain("is charging, not available"));
                Assert.That(parallel,                                         Is.True, "a second session at another station was refused");

                Assert.That(stopped,                                          Is.True);
                Assert.That(stoppedSession!.StatusAt(clock.Now),              Is.EqualTo(ChargingSessionStatus.Stopped));
                Assert.That(stoppedSession.EnergyAt(clock.Now),               Is.EqualTo(5.5m), "a stopped session charged on");
                Assert.That(stoppedSession.StoppedBy,                         Is.EqualTo("root"));

                Assert.That(stopAgain,                                        Is.False);
                Assert.That(notFound,                                         Is.False);
                Assert.That(over,                                             Does.Contain("stopped already"));
                Assert.That(noSuch,                                           Is.False);
                Assert.That(noSuchNotFound,                                   Is.True);

                // The second one charged at 150 kW: full after twelve minutes,
                // an hour ago - and so nothing charges any more.
                Assert.That(sessions.Get(second!.Id)!.StatusAt(clock.Now),    Is.EqualTo(ChargingSessionStatus.Finished));
                Assert.That(sessions.Active(clock.Now),                       Is.Empty);
                Assert.That(sessions.All().Count,                             Is.EqualTo(2));

            });

        }

        #endregion

        #region ARestartKeepsEverySession()

        /// <summary>
        /// The sessions are in their file: a phone started again has every one
        /// of them, the one still charging still charging - on from where it
        /// was, as it charged on while the phone was off.
        /// </summary>
        [Test]
        public void ARestartKeepsEverySession()
        {

            var clock = new SetClock(Monday);
            var first = SessionsIn(clock);

            first.TryStart(Holzmarkt, Holzmarkt.EVSEs[0], AllFree, "root", null, out var running, out _, out _);
            first.TryStart(Lobeda,    Lobeda.EVSEs[0],    AllFree, "root", null, out var stopped, out _, out _);

            clock.Advance(TimeSpan.FromMinutes(2));
            first.TryStop(stopped!.Id, "alice", out _, out _, out _, out _);

            clock.Advance(TimeSpan.FromMinutes(58));

            var again = SessionsIn(clock);

            Assert.Multiple(() => {

                Assert.That(again.ReadProblems,                                        Is.Empty);
                Assert.That(again.All().Count,                                         Is.EqualTo(2));
                Assert.That(again.Get(running!.Id)!.StatusAt(clock.Now),               Is.EqualTo(ChargingSessionStatus.Charging));
                Assert.That(again.Get(running.Id)!.EnergyAt(clock.Now),                Is.EqualTo(11m));
                Assert.That(again.Get(stopped.Id)!.StatusAt(clock.Now),                Is.EqualTo(ChargingSessionStatus.Stopped));
                Assert.That(again.Get(stopped.Id)!.StoppedBy,                          Is.EqualTo("alice"));
                Assert.That(again.Get(stopped.Id)!.EnergyAt(clock.Now),                Is.EqualTo(5m));
                Assert.That(again.Get(stopped.Id)!.StartedAt,                          Is.EqualTo(Monday));

            });

        }

        #endregion

        #region AFileThatCannotBeReadIsKeptAside()

        /// <summary>
        /// A file of sessions that is no JSON is moved aside and said, and the
        /// phone starts with none - rather than writing over somebody's
        /// history with the next session started.
        /// </summary>
        [Test]
        public void AFileThatCannotBeReadIsKeptAside()
        {

            var file = Path.Combine(directory, ChargingSessions.DefaultFileName);

            File.WriteAllText(file, "{ this is not what was written");

            var sessions = SessionsIn(new SetClock(Monday));

            Assert.Multiple(() => {

                Assert.That(sessions.All(),            Is.Empty);
                Assert.That(sessions.ReadProblems,     Has.Exactly(1).Contains("could not be read").And.Contains("moved to"));
                Assert.That(File.Exists(file),         Is.False);
                Assert.That(Directory.GetFiles(directory, ChargingSessions.DefaultFileName + ".unreadable-*"), Has.Length.EqualTo(1));

            });

        }

        #endregion

    }

}
