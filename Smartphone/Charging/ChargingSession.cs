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

using Newtonsoft.Json.Linq;

using cloud.charging.open.Smartphone.Map;
using cloud.charging.open.Smartphone.Provider;

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// What a charging session is doing: started and not charging yet, still
    /// charging, stopped by whoever started it, finished because the vehicle
    /// is as full as it wanted or the charge point operator says it is over -
    /// or failed, where the operator did not start it after all.
    /// </summary>
    public enum ChargingSessionStatus
    {
        Starting,
        Charging,
        Stopped,
        Finished,
        Failed
    }


    /// <summary>
    /// One charging session of this phone - simulated where there is no
    /// e-mobility provider to start a real one at; and where there is one, as
    /// the provider reports it, its figures simulated for as long as it
    /// reports nothing of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What is kept is what was true when it started - the station, its EVSE,
    /// its price - and when it started and stopped; everything else is worked
    /// out from those whenever it is asked, rather than counted up by a timer.
    /// So a session goes on charging while nobody looks, survives a restart of
    /// the phone with the right figures, and a test can say exactly what it
    /// will have charged at any minute.
    /// </para>
    /// <para>
    /// The simulation is a vehicle that takes what the EVSE gives, up to
    /// <see cref="VehicleMaxACPowerKW"/> on AC and <see cref="VehicleMaxDCPowerKW"/>
    /// on DC, at that power until <see cref="TargetEnergyKWh"/> are in the
    /// battery. That is arithmetic, not a charging curve.
    /// </para>
    /// <para>
    /// From the first report of the provider on (<see cref="Report"/>), the
    /// session is what the charge point operator told the provider: its status,
    /// what it charged and what that costs, and nothing simulated - starting
    /// while the operator reported no session yet, and with no power and no
    /// target, which OCPI does not tell.
    /// </para>
    /// <para>
    /// At a station of a roaming network it keeps what its meter values and
    /// its price are checked against later (<see cref="Evidence"/>): the
    /// proofs of the station and the price statements that held at the start.
    /// </para>
    /// <para>
    /// One the web payment page of the operator of its station started - not
    /// this phone - is known by its live link (<see cref="LiveLink"/>), which
    /// the driver handed over: starting until something comes over it,
    /// charging from then on, finished once its meter signs the end of the
    /// transaction or the station ends the stream - nothing simulated, and
    /// what it charged is what its signed documents say. Stopped where this
    /// phone stopped it through the stop link of the page, once the meter
    /// signed its end - and at once where one of its signed values is a
    /// wrong one. Disputed through the dispute link of the page where one of
    /// its signed values is a wrong one, its end value as well, or none
    /// signed its end: finished or stopped all the same.
    /// </para>
    /// </remarks>
    public sealed record ChargingSession(String           Id,
                                         String           LocationId,
                                         String           LocationName,
                                         String           Address,
                                         String           City,
                                         String           Operator,
                                         String           EVSEUid,
                                         String           EVSEId,
                                         String           Standard,
                                         String           PowerType,
                                         Decimal          MaxPowerKW,
                                         Decimal          PricePerKWh,
                                         String           Currency,
                                         Decimal          TargetEnergyKWh,
                                         DateTimeOffset   StartedAt,
                                         String           StartedBy,
                                         DateTimeOffset?  StoppedAt  = null,
                                         String?          StoppedBy  = null,
                                         ChargingTicketReference?  Ticket  = null,
                                         ChargingStartReference?   Start     = null,
                                         ProviderReport?           Report    = null,
                                         SessionEvidence?          Evidence  = null,
                                         LiveLinkFollow?           LiveLink  = null,
                                         SessionTariff?            Tariff    = null)
    {

        #region Data

        /// <summary>
        /// What the simulated vehicle takes on AC at most, in kW.
        /// </summary>
        public const Decimal VehicleMaxACPowerKW  = 11m;

        /// <summary>
        /// What the simulated vehicle takes on DC at most, in kW.
        /// </summary>
        public const Decimal VehicleMaxDCPowerKW  = 150m;

        /// <summary>
        /// How much a session charges unless it is stopped first, in kWh.
        /// </summary>
        public const Decimal DefaultTargetEnergyKWh = 30m;

        #endregion

        #region Properties

        /// <summary>
        /// The power it charges with, in kW: what the EVSE gives, as far as
        /// the vehicle takes it.
        /// </summary>
        public Decimal PowerKW
            => Math.Min(MaxPowerKW, PowerType == "DC" ? VehicleMaxDCPowerKW : VehicleMaxACPowerKW);

        /// <summary>
        /// When it is full, if nobody stops it first.
        /// </summary>
        public DateTimeOffset FullAt
            => StartedAt + TimeSpan.FromHours((Double) (TargetEnergyKWh / PowerKW));

        /// <summary>
        /// Whether its figures are simulated: no provider reported anything of
        /// it, and it follows no live link.
        /// </summary>
        public Boolean IsSimulated
            => Report is null && LiveLink is null;

        /// <summary>
        /// The tariff it is charged by: the one the operator of its station
        /// agreed for it, where it agreed one that holds, else the one of the
        /// proof of its station at the start - or null, at a demo station,
        /// which says one price per kWh alone.
        /// </summary>
        public SessionTariff? ChargedBy
            => LiveLink?.Payment?.AgreedTariff ?? Tariff;

        #endregion


        #region New(Id, Location, EVSE, StartedAt, StartedBy, Ticket = null, Start = null, Evidence = null, LiveLink = null)

        /// <summary>
        /// A session started now, at an EVSE of a location - one that says
        /// what charging there costs - with the charging ticket it was started
        /// with and its start as the provider countersigned it, where the
        /// phone has an e-mobility provider, and at a station of a roaming
        /// network with the evidence of its station - or the live link of one
        /// the web payment page of its operator started.
        /// </summary>
        public static ChargingSession New(String                    Id,
                                          ChargingLocation          Location,
                                          ChargingEVSE              EVSE,
                                          DateTimeOffset            StartedAt,
                                          String                    StartedBy,
                                          ChargingTicketReference?  Ticket    = null,
                                          ChargingStartReference?   Start     = null,
                                          SessionEvidence?          Evidence  = null,
                                          LiveLinkFollow?           LiveLink  = null)

            => new (Id,
                    Location.Id,
                    Location.Name,
                    Location.Address,
                    Location.City,
                    Location.Operator,
                    EVSE.Uid,
                    EVSE.EVSEId,
                    EVSE.Standard,
                    EVSE.PowerType,
                    EVSE.MaxPowerKW,
                    (Location.Tariff ?? throw new ArgumentException($"{Location.Name} says nothing of what charging there costs.", nameof(Location))).PricePerKWh,
                    Location.Tariff.Currency,
                    DefaultTargetEnergyKWh,
                    StartedAt,
                    StartedBy,
                    Ticket:    Ticket,
                    Start:     Start,
                    Evidence:  Evidence,
                    LiveLink:  LiveLink,
                    Tariff:    Location.Tariff.Elements is JArray elements
                                   ? new SessionTariff(Location.Tariff.Currency,
                                                       elements,
                                                       Location.Tariff.TariffId,
                                                       Location.Tariff.MinPrice,
                                                       Location.Tariff.MaxPrice,
                                                       Location.Tariff.TimeZone)
                                   : null);

        #endregion


        #region StatusAt(Now) / EndedAt(Now) / IsActiveAt(Now)

        /// <summary>
        /// What it is doing at the given time.
        /// </summary>
        public ChargingSessionStatus StatusAt(DateTimeOffset Now)
        {

            // Of a live link, stopped through its stop link once it ended: until
            // the meter signs the end, the station charges on.
            if (LiveLink is LiveLinkFollow liveLink)
                return StoppedAt is not null || liveLink.Refused
                           ? ChargingSessionStatus.Stopped
                           : liveLink.EndedAt is not null
                                 ? liveLink.StopAccepted
                                       ? ChargingSessionStatus.Stopped
                                       : ChargingSessionStatus.Finished
                                 : liveLink.Updates > 0
                                       ? ChargingSessionStatus.Charging
                                       : ChargingSessionStatus.Starting;

            if (Report is ProviderReport report)
                return StoppedAt is not null
                           ? ChargingSessionStatus.Stopped
                           : StatusOf(report);

            return StoppedAt is DateTimeOffset stopped && stopped < FullAt
                       ? ChargingSessionStatus.Stopped
                       : Now >= FullAt
                             ? ChargingSessionStatus.Finished
                             : ChargingSessionStatus.Charging;

        }

        /// <summary>
        /// What a report of the provider says the session is doing: what the
        /// operator's session says, where it reported one, else what the
        /// provider says of the start.
        /// </summary>
        private static ChargingSessionStatus StatusOf(ProviderReport Report)

            => Report.CDR is not null
                   ? ChargingSessionStatus.Finished
                   : Report.SessionStatus switch {
                         "ACTIVE"                    => ChargingSessionStatus.Charging,
                         "COMPLETED"                 => ChargingSessionStatus.Finished,
                         "INVALID"                   => ChargingSessionStatus.Failed,
                         "PENDING" or "RESERVATION"  => ChargingSessionStatus.Starting,
                         _                           => Report.State switch {
                                                            "stopped"                                       => ChargingSessionStatus.Stopped,
                                                            "refused" or "failed" or "expired" or "ended"   => ChargingSessionStatus.Failed,
                                                            _                                               => Report.ResultType is String result && result != "ACCEPTED"
                                                                                                                   ? ChargingSessionStatus.Failed
                                                                                                                   : ChargingSessionStatus.Starting
                                                        }
                     };

        /// <summary>
        /// When it ended, as seen at the given time - or null while it starts
        /// or charges.
        /// </summary>
        public DateTimeOffset? EndedAt(DateTimeOffset Now)
        {

            var status = StatusAt(Now);

            if (LiveLink is LiveLinkFollow liveLink)
                return status switch {
                           ChargingSessionStatus.Stopped   => StoppedAt ?? (liveLink.Refused ? liveLink.Wrong?.Check?.CheckedAt : liveLink.EndedAt),
                           ChargingSessionStatus.Finished  => liveLink.EndedAt,
                           _                               => null
                       };

            if (Report is ProviderReport report)
                return status switch {
                           ChargingSessionStatus.Stopped   => StoppedAt ?? report.ChargingUntil ?? report.EndedAt ?? report.ReceivedAt,
                           ChargingSessionStatus.Finished  => report.ChargingUntil ?? report.EndedAt ?? report.ReceivedAt,
                           ChargingSessionStatus.Failed    => report.EndedAt ?? report.ReceivedAt,
                           _                               => null
                       };

            return status switch {
                       ChargingSessionStatus.Stopped   => StoppedAt,
                       ChargingSessionStatus.Finished  => FullAt,
                       _                               => null
                   };

        }

        /// <summary>
        /// Whether it is still starting or charging at the given time: a
        /// session that may be stopped, and an EVSE this phone is busy at.
        /// </summary>
        public Boolean IsActiveAt(DateTimeOffset Now)

            => StatusAt(Now) is ChargingSessionStatus.Starting or ChargingSessionStatus.Charging;

        #endregion

        #region EnergyAt(Now)

        /// <summary>
        /// What it has charged by the given time, in kWh: what the provider
        /// reported, or what the simulated vehicle took.
        /// </summary>
        public Decimal EnergyAt(DateTimeOffset Now)
        {

            if (LiveLink is LiveLinkFollow liveLink)
                return liveLink.EnergyKWh ?? 0m;

            if (Report is ProviderReport report)
                return report.EnergyKWh ?? 0m;

            var until  = EndedAt(Now) ?? Now;
            var hours  = Math.Max(0, (until - StartedAt).TotalHours);

            return Math.Min(TargetEnergyKWh, Math.Round(PowerKW * (Decimal) hours, 3));

        }

        #endregion

        #region ProfileAt(Now) / CostAt(Now)

        /// <summary>
        /// What it charged when, by the given time: of a live link, what its
        /// signed values that count say, from the first; as the provider
        /// reported it, from when the operator said charging began to when it
        /// ended, or now; simulated, evenly until it was full, and parked after.
        /// </summary>
        public ChargingProfile ProfileAt(DateTimeOffset Now)
        {

            if (LiveLink is LiveLinkFollow liveLink)
            {

                var counted = liveLink.Counted.Where(reading => reading.EnergyKWh is not null).ToArray();

                return counted.Length > 0
                           ? new ChargingProfile([ .. counted.Select(reading => (reading.MeasuredAt, reading.EnergyKWh!.Value - counted[0].EnergyKWh!.Value)) ])
                           : new ChargingProfile([]);

            }

            if (Report is ProviderReport report)
                return StatusAt(Now) == ChargingSessionStatus.Starting || report.ChargingSince is not DateTimeOffset since
                           ? new ChargingProfile([])
                           : new ChargingProfile([ (since, 0m), (EndedAt(Now) ?? Now, EnergyAt(Now)) ]);

            var until  = EndedAt(Now) ?? Now;
            var full   = FullAt < until ? FullAt : until;

            return new ChargingProfile([ (StartedAt, 0m), (full, EnergyAt(full)) ], until);

        }

        /// <summary>
        /// What it costs by the given time, as the tariff it is charged by makes
        /// it of what it charged when - or null, where it is charged by none:
        /// at a demo station, which says one price per kWh alone.
        /// </summary>
        public TariffCost? CostAt(DateTimeOffset Now)

            => ChargedBy is SessionTariff tariff
                   ? TariffCost.Of(tariff, ProfileAt(Now))
                   : null;

        /// <summary>
        /// Its cost as the pages show it: what the tariff it is charged by makes
        /// of what it charged - with each part, whether it is exact and why not
        /// -, or the price per kWh of a demo station times its energy; what the
        /// provider reported, where it reported a cost.
        /// </summary>
        private JObject CostJSON(Decimal          Energy,
                                 DateTimeOffset   Now,
                                 ProviderReport?  Report  = null)
        {

            var cost  = CostAt(Now);
            var json  = new JObject(
                            new JProperty("amount",       Report?.Cost ?? cost?.Amount ?? Math.Round(Energy * PricePerKWh, 2)),
                            new JProperty("pricePerKWh",  PricePerKWh),
                            new JProperty("currency",     Report?.Cost is not null ? Report.Currency ?? Currency : cost?.Currency ?? Currency)
                        );

            if (Report is not null)
                json.Add(new JProperty("reported", Report.Cost is not null));

            if (cost is not null)
            {
                json.Add(new JProperty("tariffId",  ChargedBy?.TariffId));
                json.Add(new JProperty("agreed",    LiveLink?.Payment?.AgreedTariff is not null));
                json.Add(new JProperty("worked",    cost.ToJSON()));
            }

            return json;

        }

        #endregion


        #region ToJSON(Now)

        /// <summary>
        /// The session as the pages show it, at the given time: as the provider
        /// reported it, without a power, a target or when it will be full,
        /// where it reported anything - else simulated.
        /// </summary>
        public JObject ToJSON(DateTimeOffset Now)
        {

            if (LiveLink is LiveLinkFollow liveLink)
                return LiveLinkJSON(liveLink, Now);

            if (Report is ProviderReport report)
                return ReportedJSON(report, Now);

            var status   = StatusAt(Now);
            var ended    = EndedAt(Now);
            var energy   = EnergyAt(Now);
            var duration = (ended ?? Now) - StartedAt;

            var json = new JObject(
                           new JProperty("id",                 Id),
                           new JProperty("status",             status.ToString().ToLowerInvariant()),
                           new JProperty("simulated",          true),
                           new JProperty("location",           new JObject(
                               new JProperty("id",        LocationId),
                               new JProperty("name",      LocationName),
                               new JProperty("address",   Address),
                               new JProperty("city",      City),
                               new JProperty("operator",  Operator)
                           )),
                           new JProperty("evse",               new JObject(
                               new JProperty("uid",         EVSEUid),
                               new JProperty("evseId",      EVSEId),
                               new JProperty("standard",    Standard),
                               new JProperty("powerType",   PowerType),
                               new JProperty("maxPowerKW",  MaxPowerKW)
                           )),
                           new JProperty("startedAt",          StartedAt.ToString("o")),
                           new JProperty("startedBy",          StartedBy),
                           new JProperty("endedAt",            ended?.ToString("o")),
                           new JProperty("fullAt",             FullAt.ToString("o")),
                           new JProperty("durationSeconds",    (Int64) Math.Max(0, duration.TotalSeconds)),
                           new JProperty("powerKW",            status == ChargingSessionStatus.Charging ? PowerKW : 0m),
                           new JProperty("energyKWh",          energy),
                           new JProperty("targetEnergyKWh",    TargetEnergyKWh),
                           new JProperty("progressPercent",    Math.Round(100m * energy / TargetEnergyKWh, 1)),
                           new JProperty("cost",               CostJSON(energy, Now))
                       );

            if (status == ChargingSessionStatus.Stopped)
                json.Add(new JProperty("stoppedBy", StoppedBy));

            if (Ticket is not null)
                json.Add(new JProperty("ticket", Ticket.ToJSON()));

            if (Start is not null)
                json.Add(new JProperty("start",  Start. ToJSON()));

            return json;

        }

        /// <summary>
        /// The session as the provider reported it: its figures the operator's,
        /// its cost the operator's where it said one, else what the checked
        /// price makes of what was charged.
        /// </summary>
        private JObject ReportedJSON(ProviderReport Report, DateTimeOffset Now)
        {

            var status   = StatusAt(Now);
            var ended    = EndedAt(Now);
            var energy   = EnergyAt(Now);
            // Charged for as long as the operator says: nothing, where it never
            // said that charging began - a start that is still starting, or
            // one that failed.
            var duration = Report.ChargingSince is DateTimeOffset since && status != ChargingSessionStatus.Starting
                               ? (ended ?? Now) - since
                               : TimeSpan.Zero;

            var json = new JObject(
                           new JProperty("id",                 Id),
                           new JProperty("status",             status.ToString().ToLowerInvariant()),
                           new JProperty("simulated",          false),
                           new JProperty("location",           new JObject(
                               new JProperty("id",        LocationId),
                               new JProperty("name",      LocationName),
                               new JProperty("address",   Address),
                               new JProperty("city",      City),
                               new JProperty("operator",  Operator)
                           )),
                           new JProperty("evse",               new JObject(
                               new JProperty("uid",         EVSEUid),
                               new JProperty("evseId",      EVSEId),
                               new JProperty("standard",    Standard),
                               new JProperty("powerType",   PowerType),
                               new JProperty("maxPowerKW",  MaxPowerKW)
                           )),
                           new JProperty("startedAt",          StartedAt.ToString("o")),
                           new JProperty("startedBy",          StartedBy),
                           new JProperty("endedAt",            ended?.ToString("o")),
                           new JProperty("durationSeconds",    (Int64) Math.Max(0, duration.TotalSeconds)),
                           new JProperty("energyKWh",          energy),
                           new JProperty("cost",               CostJSON(energy, Now, Report)),
                           new JProperty("provider",           Report.ToJSON())
                       );

            if (Report.ChargingSince is DateTimeOffset chargingSince)
                json.Add(new JProperty("chargingSince", chargingSince.ToString("o")));

            if (status == ChargingSessionStatus.Stopped)
                json.Add(new JProperty("stoppedBy", StoppedBy));

            if (Ticket is not null)
                json.Add(new JProperty("ticket", Ticket.ToJSON()));

            if (Start is not null)
                json.Add(new JProperty("start",  Start. ToJSON()));

            return json;

        }

        /// <summary>
        /// The session the web payment page of the operator of its station
        /// started, as its live link says it: its energy what its signed
        /// documents say, and its cost what the price of the proof of its
        /// station makes of that - and why this phone stopped it, where one of
        /// its signed values is a wrong one. Its limit against what its signed
        /// values say ("limits"), and how it ended, once it did ("ending").
        /// </summary>
        private JObject LiveLinkJSON(LiveLinkFollow LiveLink, DateTimeOffset Now)
        {

            var status   = StatusAt(Now);
            var ended    = EndedAt(Now);
            var energy   = EnergyAt(Now);
            var since    = LiveLink.Readings.FirstOrDefault()?.MeasuredAt;
            var duration = since is DateTimeOffset chargingSince && status != ChargingSessionStatus.Starting
                               ? (ended ?? Now) - chargingSince
                               : TimeSpan.Zero;

            var json = new JObject(
                           new JProperty("id",                 Id),
                           new JProperty("status",             status.ToString().ToLowerInvariant()),
                           new JProperty("simulated",          false),
                           new JProperty("location",           new JObject(
                               new JProperty("id",        LocationId),
                               new JProperty("name",      LocationName),
                               new JProperty("address",   Address),
                               new JProperty("city",      City),
                               new JProperty("operator",  Operator)
                           )),
                           new JProperty("evse",               new JObject(
                               new JProperty("uid",         EVSEUid),
                               new JProperty("evseId",      EVSEId),
                               new JProperty("standard",    Standard),
                               new JProperty("powerType",   PowerType),
                               new JProperty("maxPowerKW",  MaxPowerKW)
                           )),
                           new JProperty("startedAt",          StartedAt.ToString("o")),
                           new JProperty("startedBy",          StartedBy),
                           new JProperty("endedAt",            ended?.ToString("o")),
                           new JProperty("durationSeconds",    (Int64) Math.Max(0, duration.TotalSeconds)),
                           new JProperty("energyKWh",          energy),
                           new JProperty("cost",               CostJSON(energy, Now)),
                           new JProperty("liveLink",           LiveLink.ToJSON())
                       );

            var liveLink = (JObject) json["liveLink"]!;

            liveLink.Add(new JProperty("limits",  new JArray(LiveLinkEnding.LimitsOf(this).Select(limit => limit.ToJSON()))));
            liveLink.Add(new JProperty("ending",  LiveLinkEnding.Of(this)?.ToJSON()));

            if (since is DateTimeOffset first)
                json.Add(new JProperty("chargingSince", first.ToString("o")));

            if (status == ChargingSessionStatus.Stopped && (StoppedBy ?? (LiveLink.Refused ? null : LiveLink.Stop?.RequestedBy)) is String stoppedBy)
                json.Add(new JProperty("stoppedBy", stoppedBy));

            if (status == ChargingSessionStatus.Stopped && LiveLink.RefusedBecause is String because)
                json.Add(new JProperty("stoppedBecause", because));

            return json;

        }

        #endregion

        #region ToStorageJSON() / TryParseStorage(JSON, out Session, out Error)

        /// <summary>
        /// What the store keeps of it: what was true when it started, and when
        /// it was stopped.
        /// </summary>
        public JObject ToStorageJSON()
        {

            var json = new JObject(
                           new JProperty("id",               Id),
                           new JProperty("locationId",       LocationId),
                           new JProperty("locationName",     LocationName),
                           new JProperty("address",          Address),
                           new JProperty("city",             City),
                           new JProperty("operator",         Operator),
                           new JProperty("evseUid",          EVSEUid),
                           new JProperty("evseId",           EVSEId),
                           new JProperty("standard",         Standard),
                           new JProperty("powerType",        PowerType),
                           new JProperty("maxPowerKW",       MaxPowerKW),
                           new JProperty("pricePerKWh",      PricePerKWh),
                           new JProperty("currency",         Currency),
                           new JProperty("targetEnergyKWh",  TargetEnergyKWh),
                           new JProperty("startedAt",        StartedAt.ToString("o")),
                           new JProperty("startedBy",        StartedBy)
                       );

            if (StoppedAt is DateTimeOffset stopped)
            {
                json.Add(new JProperty("stoppedAt", stopped.ToString("o")));
                json.Add(new JProperty("stoppedBy", StoppedBy));
            }

            if (Ticket is not null)
                json.Add(new JProperty("ticket", Ticket.ToJSON()));

            if (Start is not null)
                json.Add(new JProperty("start",  Start. ToJSON()));

            if (Report is not null)
                json.Add(new JProperty("report", Report.ToStorageJSON()));

            if (Evidence is not null)
                json.Add(new JProperty("evidence", Evidence.ToJSON()));

            if (LiveLink is not null)
                json.Add(new JProperty("liveLink", LiveLink.ToStorageJSON()));

            if (Tariff is not null)
                json.Add(new JProperty("tariff", Tariff.ToJSON()));

            return json;

        }

        /// <summary>
        /// One session as the store kept it.
        /// </summary>
        public static Boolean TryParseStorage(JObject                                    JSON,
                                              [NotNullWhen(true)]  out ChargingSession?  Session,
                                              [NotNullWhen(false)] out String?           Error)
        {

            Session = null;
            Error   = null;

            try
            {

                String  Text(String Name)     => JSON.Value<String>(Name) is { Length: > 0 } text ? text : throw new FormatException($"'{Name}' is missing.");
                Decimal Number(String Name)   => JSON[Name] is JToken token && token.Type is JTokenType.Integer or JTokenType.Float ? token.Value<Decimal>() : throw new FormatException($"'{Name}' is no number.");
                DateTimeOffset Time(String Name) => DateTimeOffset.Parse(Text(Name), System.Globalization.CultureInfo.InvariantCulture);

                Session = new ChargingSession(
                              Text  ("id"),
                              Text  ("locationId"),
                              Text  ("locationName"),
                              JSON.Value<String>("address")  ?? "",
                              JSON.Value<String>("city")     ?? "",
                              JSON.Value<String>("operator") ?? "",
                              Text  ("evseUid"),
                              Text  ("evseId"),
                              Text  ("standard"),
                              Text  ("powerType"),
                              Number("maxPowerKW"),
                              Number("pricePerKWh"),
                              Text  ("currency"),
                              Number("targetEnergyKWh"),
                              Time  ("startedAt"),
                              Text  ("startedBy"),
                              JSON["stoppedAt"] is JToken { Type: JTokenType.String } ? Time("stoppedAt") : null,
                              JSON.Value<String>("stoppedBy"),
                              ChargingTicketReference.TryParse(JSON["ticket"]),
                              ChargingStartReference. TryParse(JSON["start"]),
                              ProviderReport.         TryParseStorage(JSON["report"]),
                              SessionEvidence.        TryParse(JSON["evidence"]),
                              LiveLinkFollow.         TryParse(JSON["liveLink"]),
                              SessionTariff.          TryParse(JSON["tariff"])
                          );

                if (Session.MaxPowerKW <= 0 || Session.TargetEnergyKWh <= 0)
                    throw new FormatException("its power and its target are more than nothing.");

                return true;

            }
            catch (Exception e) when (e is FormatException or InvalidCastException or ArgumentException)
            {
                Session = null;
                Error   = $"A session '{JSON.Value<String>("id") ?? "without an id"}' could not be read: {e.Message}";
                return false;
            }

        }

        #endregion

    }

}
