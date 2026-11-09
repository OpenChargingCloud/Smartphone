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

            Assert.That(sessions.TryStart(Holzmarkt, Holzmarkt.EVSEs[0], AllFree, "root", null, null, out var started, out var error, out _), Is.True, error);

            var again     = sessions.TryStart(Holzmarkt, Holzmarkt.EVSEs[0], AllFree, "root", null, null, out _, out var twice, out _);
            var busy      = sessions.TryStart(Lobeda, Lobeda.EVSEs[0], (_, _) => EVSEStatus.Charging, "root", null, null, out _, out var notFree, out _);
            var parallel  = sessions.TryStart(Lobeda, Lobeda.EVSEs[1], AllFree, "root", null, null, out var second, out _, out _);

            clock.Advance(TimeSpan.FromMinutes(30));

            var stopped   = sessions.TryStop(started!.Id, "root", null, out var stoppedSession, out _, out _, out _);

            clock.Advance(TimeSpan.FromMinutes(30));

            var stopAgain = sessions.TryStop(started.Id, "root", null, out _, out var over, out var notFound, out _);
            var noSuch    = sessions.TryStop("NOSUCHONE", "root", null, out _, out _, out var noSuchNotFound, out _);

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

            first.TryStart(Holzmarkt, Holzmarkt.EVSEs[0], AllFree, "root", null, null, out var running, out _, out _);
            first.TryStart(Lobeda,    Lobeda.EVSEs[0],    AllFree, "root", null, null, out var stopped, out _, out _);

            clock.Advance(TimeSpan.FromMinutes(2));
            first.TryStop(stopped!.Id, "alice", null, out _, out _, out _, out _);

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

        #region ALiveLinkKeepsHowEachOfItsDocumentsHeld()

        /// <summary>
        /// A live link keeps how each signed document it brought held, with the
        /// document: kept and read again as it was - and the check of its start
        /// a store kept beside its documents, before each kept its own, is read
        /// as the check of the document of its start. A document that held or
        /// did not stays as it was.
        /// </summary>
        [Test]
        public void ALiveLinkKeepsHowEachOfItsDocumentsHeld()
        {

            var at      = new DateTimeOffset(2026, 10, 10, 8, 10, 5, TimeSpan.Zero);
            var meter   = TestRoamingNetwork.NewMeterKey();
            var begin   = TestRoamingNetwork.OCMF(meter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (at.AddMinutes(-10), "B", 1000m));
            var after   = TestRoamingNetwork.OCMF(meter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (at,                 "C", 1012.5m));
            var link    = new JObject(new JProperty("@context", "https://open.charging.cloud/contexts/chargeTransparency/live/link/1.0"));

            var follow  = new LiveLinkFollow(link, null, at.AddMinutes(-11)).
                              WithUpdate(at.AddMinutes(-10), "https", "https://pay.example/live", "OCMF", [ begin ]).
                              WithUpdate(at,                 "https", "https://pay.example/live", "OCMF", [ after ]).
                              WithChecks([ (begin, new LiveLinkValueCheck(at, true,  null, "DC-2026-001", "meter-dc-1", "95ceb48c", "ValidSignature")),
                                           (after, new LiveLinkValueCheck(at, false, "The meter counts backwards.", "DC-2026-001")) ]);

            var read    = LiveLinkFollow.TryParse(follow.ToStorageJSON())!;

            // As a store kept it beside its documents.
            var before  = follow.ToStorageJSON();

            foreach (var document in before["documents"]!.Children<JObject>())
                document.Remove("check");

            before.Add(new JProperty("start", new JObject(new JProperty("checkedAt", at.ToString("o")), new JProperty("holds", true),
                                                          new JProperty("keyId", "95ceb48c"), new JProperty("text", begin))));

            var older   = LiveLinkFollow.TryParse(before)!;

            Assert.Multiple(() => {

                Assert.That(follow.Start?.Holds,                Is.True);
                Assert.That(follow.Wrong?.Text,                 Is.EqualTo(after));
                Assert.That(follow.RefusedBecause,              Does.StartWith("A signed meter value after its start is a wrong one: The meter counts backwards."));
                Assert.That(follow.ToCheck,                     Is.Empty, "a document that held or did not is checked again");

                Assert.That(read.Start?.KeyId,                  Is.EqualTo("95ceb48c"));
                Assert.That(read.Wrong?.Check?.Error,           Is.EqualTo("The meter counts backwards."));
                Assert.That(read.ToJSON()["values"]?["wrong"]?.Value<Int32>(), Is.EqualTo(1));

                Assert.That(older.Start?.Holds,                 Is.True,  "the check of its start kept beside its documents is lost");
                Assert.That(older.Start?.KeyId,                 Is.EqualTo("95ceb48c"));
                Assert.That(older.Wrong,                        Is.Null);
                Assert.That(older.ToCheck.Select(document => document.Text), Is.EqualTo(new[] { after }));

                Assert.That(follow.WithChecks([ (after, new LiveLinkValueCheck(at, true, null)) ]).Wrong?.Text, Is.EqualTo(after), "a wrong one became one that holds");

            });

        }

        #endregion

        #region AnEndIsComparedWithTheLimitTheDriverChose()

        /// <summary>
        /// A live link keeps its stop link, the limit the driver chose and the
        /// stop this phone asked for, and shows the host of its stop link only.
        /// Its end is compared with each part of its limit: reached within a
        /// hundredth of it, not reached below - the state of charge no meter
        /// measures not at all -, and stopped where this phone stopped it.
        /// </summary>
        [Test]
        public void AnEndIsComparedWithTheLimitTheDriverChose()
        {

            var at      = new DateTimeOffset(2026, 10, 10, 8, 10, 5, TimeSpan.Zero);
            var meter   = TestRoamingNetwork.NewMeterKey();
            var begin   = TestRoamingNetwork.OCMF(meter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (at.AddMinutes(-10), "B", 1000m));
            var link    = new JObject(new JProperty("@context", "https://open.charging.cloud/contexts/chargeTransparency/live/link/1.0"));
            var limit   = new TransactionLimit(MaxEnergy: 20000m, MaxCost: 13m, MaxTime: 3600, MaxSoC: 80);

            LiveLinkFollow Ended(Decimal EnergyKWh)
                => new LiveLinkFollow(link, null, at.AddMinutes(-11), StopLink: "https://pay.example/sessions/S1/stop", Limit: limit).
                       WithUpdate(at.AddMinutes(-10), "https", "https://pay.example/live", "OCMF", [ begin ]).
                       WithUpdate(at,                 "https", "https://pay.example/live", "OCMF", [ TestRoamingNetwork.OCMF(meter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (at, "E", EnergyKWh)) ]);

            ChargingSession SessionOf(LiveLinkFollow Follow)
                => new ("S1", "pool-1", "Jena", "Holzmarkt 1", "Jena", "GraphDefined", "dc-1", TestRoamingNetwork.DCEVSE, "CCS", "DC",
                        150m, 0.65m, "EUR", ChargingSession.DefaultTargetEnergyKWh, at.AddMinutes(-11), "driver1", LiveLink: Follow);

            var atLimit  = SessionOf(Ended(1019.850m));
            var below    = SessionOf(Ended(1019.700m));
            var stop     = new LiveLinkStop(at.AddMinutes(-1), "driver1", true, "202 Accepted: The station stops.");
            var stopped  = SessionOf(Ended(1019.700m) with { Stop = stop });
            var read     = LiveLinkFollow.TryParse(stopped.LiveLink!.ToStorageJSON())!;
            var limits   = LiveLinkEnding.LimitsOf(atLimit);

            Assert.Multiple(() => {

                Assert.That(limits.Select(one => one.Kind),     Is.EqualTo(new[] { "energy", "cost", "time", "soc" }));
                Assert.That(limits.Select(one => one.Value),    Is.EqualTo(new Decimal?[] { 19.85m, 12.90m, 600m, null }));
                Assert.That(limits.Select(one => one.Reached),  Is.EqualTo(new Boolean?[] { true, true, false, null }), "within a hundredth of 20 kWh and 13.00 EUR, not of an hour");

                Assert.That(LiveLinkEnding.Of(atLimit)!.Reason,   Is.EqualTo("limit"));
                Assert.That(LiveLinkEnding.Of(atLimit)!.Because,  Does.Contain("at its limit of 20 kWh and 13.00 EUR: 19.85 kWh, 12.90 EUR"));
                Assert.That(atLimit.StatusAt(at),                 Is.EqualTo(ChargingSessionStatus.Finished));

                Assert.That(LiveLinkEnding.Of(below)!.Reason,     Is.EqualTo("unknown"), "19.7 kWh reached a limit of 20 kWh, or its state of charge is known");
                Assert.That(LiveLinkEnding.Of(below)!.Because,    Does.Contain("before its limit of 20 kWh and 13.00 EUR and 1 h - 19.7 kWh, 12.80 EUR, 10 min").And.Contain("80 % state of charge"));
                Assert.That(LiveLinkEnding.Of(SessionOf(Ended(1019.700m) with { Limit = limit with { MaxSoC = null } }))!.Reason, Is.EqualTo("other"));

                Assert.That(LiveLinkEnding.Of(stopped)!.Reason,   Is.EqualTo("stopped"));
                Assert.That(stopped.StatusAt(at),                 Is.EqualTo(ChargingSessionStatus.Stopped));
                Assert.That(stopped.EndedAt(at),                  Is.EqualTo(at), "it ended once the meter signed its end");

                Assert.That(read.StopLink,                        Is.EqualTo("https://pay.example/sessions/S1/stop"));
                Assert.That(read.Limit,                           Is.EqualTo(limit));
                Assert.That(read.Stop,                            Is.EqualTo(stop));
                Assert.That(read.ToJSON()["stopLink"]?.Value<String>(), Is.EqualTo("pay.example"), "the stop link itself is shown");

                Assert.That(TransactionLimit.TryParse(null, out var none, out _) && none is null, Is.True);
                Assert.That(TransactionLimit.TryParse(limit.ToJSON(), out var again, out _) && again == limit, Is.True, "a limit is not read as it was written");

            });

        }

        #endregion

        #region AWrongEndOrNoneIsDisputed()

        /// <summary>
        /// A wrong end value stops nothing and is disputed, with the signed
        /// values of the start and the end; it counts for nothing, and says
        /// nothing of how the session ended. So is a whole transaction that
        /// came at its end alone, and is a wrong one. A stream the station
        /// ended with no end value signed is disputed as well, with the last
        /// value that came - and a live link keeps its dispute link and the
        /// dispute it sent, showing the host of the link only, and the
        /// payment reference the page gave, and what each value named.
        /// </summary>
        [Test]
        public void AWrongEndOrNoneIsDisputed()
        {

            var at      = new DateTimeOffset(2026, 10, 10, 8, 10, 5, TimeSpan.Zero);
            var meter   = TestRoamingNetwork.NewMeterKey();
            var begin   = TestRoamingNetwork.OCMF(meter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (at.AddMinutes(-10), "B", 1000m));
            var after   = TestRoamingNetwork.OCMF(meter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (at.AddMinutes(-5),  "C", 1006m));
            var end     = TestRoamingNetwork.OCMF(meter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (at,                 "E", 1012m));
            var whole   = TestRoamingNetwork.OCMF(meter, TestRoamingNetwork.DCMeterSerial, "web-payment-1", (at.AddMinutes(-10), "B", 1000m), (at, "E", 1012m));
            var link    = new JObject(new JProperty("@context", "https://open.charging.cloud/contexts/chargeTransparency/live/link/1.0"));
            var holds   = new LiveLinkValueCheck(at, true,  null);
            var wrong   = new LiveLinkValueCheck(at, false, "The meter counts backwards.", Identification: "web-payment-1");

            ChargingSession SessionOf(LiveLinkFollow Follow)
                => new ("S1", "pool-1", "Jena", "Holzmarkt 1", "Jena", "GraphDefined", "dc-1", TestRoamingNetwork.DCEVSE, "CCS", "DC",
                        150m, 0.65m, "EUR", ChargingSession.DefaultTargetEnergyKWh, at.AddMinutes(-11), "driver1", LiveLink: Follow);

            var wrongEnd  = new LiveLinkFollow(link, null, at.AddMinutes(-11), Limit: new TransactionLimit(MaxEnergy: 12000m), DisputeLink: "https://pay.example/sessions/S1/dispute",
                                               PaymentReference: "web-payment-1").
                                WithUpdate(at.AddMinutes(-10), "https", "https://pay.example/live", "OCMF", [ begin ]).
                                WithUpdate(at.AddMinutes(-5),  "https", "https://pay.example/live", "OCMF", [ after ]).
                                WithUpdate(at,                 "https", "https://pay.example/live", "OCMF", [ end ]).
                                WithChecks([ (begin, holds), (after, holds), (end, wrong) ]);

            var onlyWhole = new LiveLinkFollow(link, null, at.AddMinutes(-11)).
                                WithUpdate(at, "https", "https://pay.example/live", "OCMF", [ whole ]).
                                WithChecks([ (whole, wrong) ]);

            var noEnd     = new LiveLinkFollow(link, null, at.AddMinutes(-11)).
                                WithUpdate(at.AddMinutes(-10), "https", "https://pay.example/live", "OCMF", [ begin ]).
                                WithUpdate(at.AddMinutes(-5),  "https", "https://pay.example/live", "OCMF", [ after ]).
                                WithChecks([ (begin, holds), (after, holds) ])
                                with { EndedAt = at, EndedBecause = "The station ended the stream of its live link." };

            var dispute   = new LiveLinkDispute(at.AddMinutes(1), "this phone", wrongEnd.DisputeBecause!, true, "200 OK: The dispute is taken.");
            var read      = LiveLinkFollow.TryParse((wrongEnd with { Dispute = dispute }).ToStorageJSON())!;
            var ending    = LiveLinkEnding.Of(SessionOf(wrongEnd))!;

            Assert.Multiple(() => {

                Assert.That(wrongEnd.ToCheck,                    Is.Empty, "an end value is not checked");
                Assert.That(wrongEnd.Refused,                    Is.False, "a wrong end value stops the session");
                Assert.That(wrongEnd.WrongEnd?.Text,             Is.EqualTo(end));
                Assert.That(wrongEnd.End?.Holds,                 Is.False);
                Assert.That(wrongEnd.DisputeBecause,             Is.EqualTo("Its signed end meter value is a wrong one: The meter counts backwards."));
                Assert.That(wrongEnd.Disputed,                   Is.EqualTo(new[] { begin, end }));
                Assert.That(wrongEnd.EnergyKWh,                  Is.EqualTo(6m), "the wrong end value counts");
                Assert.That(wrongEnd.DisputePending,             Is.True);
                Assert.That(ending.Reason,                       Is.EqualTo("wrong"));
                Assert.That(ending.Transaction,                  Is.Null);
                Assert.That(ending.Limits.Single().Value,        Is.EqualTo(6m), "a limit compared with a wrong end value");
                Assert.That(SessionOf(wrongEnd).StatusAt(at),    Is.EqualTo(ChargingSessionStatus.Finished));

                Assert.That(onlyWhole.StartDocument?.Document.Text, Is.EqualTo(whole));
                Assert.That(onlyWhole.Refused,                   Is.False, "a whole transaction at its end stops it");
                Assert.That(onlyWhole.DisputeBecause,            Does.StartWith("Its signed end meter value is a wrong one"));
                Assert.That(onlyWhole.Disputed,                  Is.EqualTo(new[] { whole }));
                Assert.That(onlyWhole.DisputePending,            Is.False, "disputed with no dispute link");

                Assert.That(noEnd.End,                           Is.Null);
                Assert.That(noEnd.DisputeBecause,                Does.Contain("signed no end of the transaction"));
                Assert.That(noEnd.Disputed,                      Is.EqualTo(new[] { begin, after }));
                Assert.That(LiveLinkEnding.Of(SessionOf(noEnd))!.Because, Does.Contain("signed no end"));

                Assert.That(read.DisputeLink,                    Is.EqualTo("https://pay.example/sessions/S1/dispute"));
                Assert.That(read.Dispute,                        Is.EqualTo(dispute));
                Assert.That(read.DisputePending,                 Is.False, "disputed again");
                Assert.That(read.ToJSON()["disputeLink"]?.Value<String>(), Is.EqualTo("pay.example"), "the dispute link itself is shown");
                Assert.That(read.ToJSON()["end"]?["holds"]?.Value<Boolean?>(), Is.False);
                Assert.That(read.PaymentReference,               Is.EqualTo("web-payment-1"));
                Assert.That(read.End?.Identification,            Is.EqualTo("web-payment-1"), "what its end value named");
                Assert.That(read.ToJSON()["paymentReference"]?.Value<String>(), Is.EqualTo("web-payment-1"));
                Assert.That(read.ToJSON()["end"]?["identification"]?.Value<String>(), Is.EqualTo("web-payment-1"));

            });

        }

        #endregion

        #region AHandOverNamesWhatThePageGave()

        /// <summary>
        /// A hand-over names its @context and the live link, and may name the
        /// stop link, the dispute link, the payment reference and the limit -
        /// each one that is one, and passes over what else it says. It reads
        /// as it is written.
        /// </summary>
        [Test]
        public void AHandOverNamesWhatThePageGave()
        {

            JObject HandOver(params JProperty[] Properties)
            {
                var json = new JObject(new JProperty("@context", WebPaymentHandOver.JSONLDContext),
                                       new JProperty("liveLink", "https://pay.example/liveLinks/S1"));
                foreach (var property in Properties)
                    json[property.Name] = property.Value;
                return json;
            }

            String? ErrorOf(JObject JSON)
                => WebPaymentHandOver.TryParse(JSON, out _, out var error) ? null : error;

            var full = new WebPaymentHandOver(new JValue("https://pay.example/liveLinks/S1"),
                                              "https://pay.example/sessions/S1/stop",
                                              "https://pay.example/sessions/S1/dispute",
                                              "payment-S1",
                                              new TransactionLimit(MaxEnergy: 20000m));

            var json = full.ToJSON();
            json.Add(new JProperty("validUntil", "2026-11-10T00:00:00Z"));

            Assert.Multiple(() => {

                Assert.That(WebPaymentHandOver.TryParse(json, out var read, out var error), Is.True, error);
                Assert.That(read!.LiveLink.Value<String>(),    Is.EqualTo("https://pay.example/liveLinks/S1"));
                Assert.That(read.StopLink,                     Is.EqualTo(full.StopLink));
                Assert.That(read.DisputeLink,                  Is.EqualTo(full.DisputeLink));
                Assert.That(read.PaymentReference,             Is.EqualTo("payment-S1"));
                Assert.That(read.Limit,                        Is.EqualTo(full.Limit));
                Assert.That(json["@context"]!.Value<String>(), Is.EqualTo("https://open.charging.cloud/contexts/wwcp/webPayment/handOver/v1"));

                Assert.That(WebPaymentHandOver.TryParse(HandOver(), out var bare, out _), Is.True);
                Assert.That(bare!.StopLink,                    Is.Null);
                Assert.That(bare.Limit,                        Is.Null);

                Assert.That(ErrorOf(HandOver(new JProperty("@context", "https://open.charging.cloud/contexts/chargeTransparency/live/link/1.0"))),
                                                                                     Does.StartWith("It is no hand-over of a web payment page"));
                Assert.That(ErrorOf(new JObject(new JProperty("@context", WebPaymentHandOver.JSONLDContext))),   Does.Contain("'liveLink'"));
                Assert.That(ErrorOf(HandOver(new JProperty("liveLink", 42))),                                    Does.Contain("'liveLink'"));
                Assert.That(ErrorOf(HandOver(new JProperty("liveLink", " "))),                                   Does.Contain("'liveLink'"));
                Assert.That(ErrorOf(HandOver(new JProperty("stopLink", "http://pay.example/stop"))),             Does.Contain("'stopLink' 'http://pay.example/stop' is no https URL"));
                Assert.That(ErrorOf(HandOver(new JProperty("disputeLink", new JArray()))),                       Does.Contain("'disputeLink' is a text"));
                Assert.That(ErrorOf(HandOver(new JProperty("paymentReference", "a\u0007b"))),                    Does.Contain("is no payment reference"));
                Assert.That(ErrorOf(HandOver(new JProperty("paymentReference", new String('p', 256)))),          Does.Contain("is no payment reference"));
                Assert.That(ErrorOf(HandOver(new JProperty("limit", new JObject(new JProperty("maxSoC", 120))))), Does.Contain("'limit.maxSoC'"));
                Assert.That(ErrorOf(HandOver(new JProperty("stopLink", "http://127.0.0.1:8080/stop"))),          Is.Null, "a stop link on this machine's loopback");

            });

        }

        #endregion

        #region WhatThePageGaveIsBoundByTheHostOfItsWebPaymentPage()

        /// <summary>
        /// A stop link and a dispute link are bound to the operator where they
        /// are at the host of the web payment page the proof of the station
        /// names - its port as well -, the payment reference where it came
        /// with a hand-over from there. Nothing is bound where the proof named
        /// no web payment page. The hand-over is kept, and shown by its host
        /// alone.
        /// </summary>
        [Test]
        public void WhatThePageGaveIsBoundByTheHostOfItsWebPaymentPage()
        {

            var at    = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
            var link  = new JObject(new JProperty("@context", "https://open.charging.cloud/contexts/chargeTransparency/live/link/1.0"));

            var handedOver  = new LiveLinkFollow(link, null, at,
                                                 StopLink:          "https://PAY.example/sessions/S1/stop",
                                                 DisputeLink:       "https://pay.example:8443/sessions/S1/dispute",
                                                 PaymentReference:  "payment-S1",
                                                 WebPaymentPage:    "https://pay.example/stations/S1",
                                                 HandOver:          "https://pay.example/handOvers/S1");

            var typed       = handedOver with { HandOver = null };
            var noPage      = handedOver with { WebPaymentPage = null };
            var read        = LiveLinkFollow.TryParse(handedOver.ToStorageJSON())!;
            var shown       = read.ToJSON();

            Assert.Multiple(() => {

                Assert.That(handedOver.StopLinkBound,          Is.True,  "a stop link at the host of the web payment page, in other letters");
                Assert.That(handedOver.DisputeLinkBound,       Is.False, "a dispute link at another port");
                Assert.That(handedOver.PaymentReferenceBound,  Is.True);

                Assert.That(typed.StopLinkBound,               Is.True,  "a stop link typed at the host of the web payment page");
                Assert.That(typed.PaymentReferenceBound,       Is.False, "a payment reference typed");

                Assert.That(noPage.StopLinkBound,              Is.False);
                Assert.That(noPage.PaymentReferenceBound,      Is.False);

                Assert.That(read.WebPaymentPage,               Is.EqualTo("https://pay.example/stations/S1"));
                Assert.That(read.HandOver,                     Is.EqualTo("https://pay.example/handOvers/S1"));
                Assert.That(read.PaymentReferenceBound,        Is.True);
                Assert.That(shown["handOver"]!.Value<String>(),        Is.EqualTo("pay.example"));
                Assert.That(shown["webPaymentHost"]!.Value<String>(),  Is.EqualTo("pay.example"));
                Assert.That(shown["bound"]!["stopLink"]!.Value<Boolean>(),          Is.True);
                Assert.That(shown["bound"]!["disputeLink"]!.Value<Boolean>(),       Is.False);
                Assert.That(shown["bound"]!["paymentReference"]!.Value<Boolean>(),  Is.True);
                Assert.That(shown.ToString(),                  Does.Not.Contain("/handOvers/S1"), "the hand-over itself is shown");

                Assert.That(new LiveLinkFollow(link, null, at).ToJSON()["bound"]!["stopLink"]!.Value<Boolean>(), Is.False);

            });

        }

        #endregion

    }

}
