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

using cloud.charging.open.Smartphone.Charging;
using cloud.charging.open.Smartphone.Map;

#endregion

namespace cloud.charging.open.Smartphone
{

    /// <summary>
    /// Why a charging session was not started or not stopped.
    /// </summary>
    public enum ChargingRefusal
    {
        /// <summary>Nothing was refused.</summary>
        None,
        /// <summary>The station, its EVSE or the session is not there.</summary>
        NotFound,
        /// <summary>It is there, and not in a state to do it: busy, out of order, over already.</summary>
        Conflict,
        /// <summary>The sessions could not be written down: nothing about the request was wrong.</summary>
        NotSaved
    }


    public partial class Smartphone
    {

        #region TryStartCharging(LocationId, EVSEUid, Who, out Session, out Error, out Refusal)

        /// <summary>
        /// Start charging at an EVSE of a station of the map - simulated, for
        /// as long as there is no e-mobility provider to ask.
        /// </summary>
        /// <param name="LocationId">The station, by the id the map gave it.</param>
        /// <param name="EVSEUid">Its EVSE, by the uid the map gave it.</param>
        /// <param name="Who">Who asked, for the log and the session.</param>
        /// <param name="Session">The session started.</param>
        /// <param name="Error">Why none was.</param>
        /// <param name="Refusal">What kind of no it was.</param>
        public Boolean TryStartCharging(String                                     LocationId,
                                        String                                     EVSEUid,
                                        String                                     Who,
                                        [NotNullWhen(true)]  out ChargingSession?  Session,
                                        [NotNullWhen(false)] out String?           Error,
                                        out ChargingRefusal                        Refusal)
        {

            Session  = null;
            Refusal  = ChargingRefusal.None;

            var location = DemoLocations.Location(LocationId);

            if (location is null)
            {
                Refusal  = ChargingRefusal.NotFound;
                Error    = $"There is no charging station '{LocationId}' on the map.";
                return false;
            }

            var evse = location.EVSE(EVSEUid);

            if (evse is null)
            {
                Refusal  = ChargingRefusal.NotFound;
                Error    = $"{location.Name} has no charge point '{EVSEUid}'.";
                return false;
            }

            if (!Sessions.TryStart(location, evse, DemoLocations.StatusOf, Who, out Session, out Error, out var notSaved))
            {
                Refusal = notSaved ? ChargingRefusal.NotSaved : ChargingRefusal.Conflict;
                return false;
            }

            Log.Notice($"'{Who}' started charging at {location.Name}, {evse.EVSEId}, with {Session.PowerKW:0.#} kW until {Session.TargetEnergyKWh:0.#} kWh - " +
                       $"simulated: no e-mobility provider was asked. The session is {Session.Id}.",
                       "charging", "web");

            return true;

        }

        #endregion

        #region TryStopCharging(Id, Who, out Session, out Error, out Refusal)

        /// <summary>
        /// Stop a charging session that is still charging.
        /// </summary>
        public Boolean TryStopCharging(String                                     Id,
                                       String                                     Who,
                                       [NotNullWhen(true)]  out ChargingSession?  Session,
                                       [NotNullWhen(false)] out String?           Error,
                                       out ChargingRefusal                        Refusal)
        {

            Refusal = ChargingRefusal.None;

            if (!Sessions.TryStop(Id, Who, out Session, out Error, out var notFound, out var notSaved))
            {
                Refusal = notFound ? ChargingRefusal.NotFound
                        : notSaved ? ChargingRefusal.NotSaved
                        :            ChargingRefusal.Conflict;
                return false;
            }

            var now = TimeProvider.GetUtcNow();

            Log.Notice($"'{Who}' stopped charging at {Session.LocationName}, {Session.EVSEId}, after {Session.EnergyAt(now):0.###} kWh. The session was {Session.Id}.",
                       "charging", "web");

            return true;

        }

        #endregion

        #region SessionsJSON(OnlyActive)

        /// <summary>
        /// The charging sessions of this phone, the newest first - or only the
        /// ones still charging.
        /// </summary>
        public JObject SessionsJSON(Boolean OnlyActive = false)
        {

            var now     = TimeProvider.GetUtcNow();
            var all     = Sessions.All();
            var shown   = OnlyActive ? all.Where(session => session.IsActiveAt(now)).ToArray() : all;

            return new JObject(
                       new JProperty("timestamp",  now.ToString("o")),
                       new JProperty("simulated",  true),
                       new JProperty("active",     all.Count(session => session.IsActiveAt(now))),
                       new JProperty("total",      all.Count),
                       new JProperty("sessions",   new JArray(shown.Select(session => session.ToJSON(now))))
                   );

        }

        #endregion

    }

}
