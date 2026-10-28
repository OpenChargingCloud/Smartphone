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

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// What a charging session is doing: still charging, stopped by whoever
    /// started it, or finished because the vehicle is as full as it wanted.
    /// </summary>
    public enum ChargingSessionStatus
    {
        Charging,
        Stopped,
        Finished
    }


    /// <summary>
    /// One charging session of this phone - simulated, for as long as there
    /// is no e-mobility provider to start a real one at.
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
                                         String?          StoppedBy  = null)
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

        #endregion


        #region New(Id, Location, EVSE, StartedAt, StartedBy)

        /// <summary>
        /// A session started now, at an EVSE of a location - one that says
        /// what charging there costs.
        /// </summary>
        public static ChargingSession New(String            Id,
                                          ChargingLocation  Location,
                                          ChargingEVSE      EVSE,
                                          DateTimeOffset    StartedAt,
                                          String            StartedBy)

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
                    StartedBy);

        #endregion


        #region StatusAt(Now) / EndedAt(Now) / IsActiveAt(Now)

        /// <summary>
        /// What it is doing at the given time.
        /// </summary>
        public ChargingSessionStatus StatusAt(DateTimeOffset Now)

            => StoppedAt is DateTimeOffset stopped && stopped < FullAt
                   ? ChargingSessionStatus.Stopped
                   : Now >= FullAt
                         ? ChargingSessionStatus.Finished
                         : ChargingSessionStatus.Charging;

        /// <summary>
        /// When it ended, as seen at the given time - or null while it charges.
        /// </summary>
        public DateTimeOffset? EndedAt(DateTimeOffset Now)

            => StatusAt(Now) switch {
                   ChargingSessionStatus.Stopped   => StoppedAt,
                   ChargingSessionStatus.Finished  => FullAt,
                   _                               => null
               };

        /// <summary>
        /// Whether it is still charging at the given time.
        /// </summary>
        public Boolean IsActiveAt(DateTimeOffset Now)

            => StatusAt(Now) == ChargingSessionStatus.Charging;

        #endregion

        #region EnergyAt(Now)

        /// <summary>
        /// What it has charged by the given time, in kWh.
        /// </summary>
        public Decimal EnergyAt(DateTimeOffset Now)
        {

            var until  = EndedAt(Now) ?? Now;
            var hours  = Math.Max(0, (until - StartedAt).TotalHours);

            return Math.Min(TargetEnergyKWh, Math.Round(PowerKW * (Decimal) hours, 3));

        }

        #endregion


        #region ToJSON(Now)

        /// <summary>
        /// The session as the pages show it, at the given time.
        /// </summary>
        public JObject ToJSON(DateTimeOffset Now)
        {

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
                           new JProperty("cost",               new JObject(
                               new JProperty("amount",       Math.Round(energy * PricePerKWh, 2)),
                               new JProperty("pricePerKWh",  PricePerKWh),
                               new JProperty("currency",     Currency)
                           ))
                       );

            if (status == ChargingSessionStatus.Stopped)
                json.Add(new JProperty("stoppedBy", StoppedBy));

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
                              JSON.Value<String>("stoppedBy")
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
