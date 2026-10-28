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
using cloud.charging.open.Smartphone.Provider;

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
        NotSaved,
        /// <summary>The e-mobility provider issued no charging ticket for it, or could not be reached.</summary>
        Provider
    }


    /// <summary>
    /// What came of "Charge now!": the session started, or why none was.
    /// </summary>
    public sealed record ChargingStart(ChargingSession?  Session,
                                       String?           Error,
                                       ChargingRefusal   Refusal)
    {

        public static ChargingStart Refused(ChargingRefusal Refusal, String Error)
            => new (null, Error, Refusal);

    }


    public partial class Smartphone
    {

        #region StartChargingAsync(LocationId, EVSEUid, Who, CancellationToken = default)

        /// <summary>
        /// Start charging at an EVSE of a station of the map - simulated, for
        /// as long as the e-mobility provider takes no start requests. At a
        /// station of a roaming network, only once the proof of its station was
        /// checked and while it holds, for the price it gives. With an account
        /// at an e-mobility provider, with a charging ticket of its own, asked
        /// for right before the start and kept with the session.
        /// </summary>
        /// <param name="LocationId">The station, by the id the map gave it.</param>
        /// <param name="EVSEUid">Its EVSE, by the uid the map gave it.</param>
        /// <param name="Who">Who asked, for the log and the session.</param>
        /// <param name="CancellationToken">To stop asking the provider.</param>
        public async Task<ChargingStart> StartChargingAsync(String             LocationId,
                                                            String             EVSEUid,
                                                            String             Who,
                                                            CancellationToken  CancellationToken = default)
        {

            var anchor    = RoamingNetwork.TrustAnchor;
            var location  = anchor is null
                                ? DemoLocations.Location(LocationId)
                                : RoamingLocation(LocationId);

            if (location is null)
                return ChargingStart.Refused(ChargingRefusal.NotFound, $"There is no charging station '{LocationId}' on the map.");

            var evse = location.EVSE(EVSEUid);

            if (evse is null)
                return ChargingStart.Refused(ChargingRefusal.NotFound, $"{location.Name} has no charge point '{EVSEUid}'.");

            Func<ChargingEVSE, DateTimeOffset, String> statusOf = DemoLocations.StatusOf;

            // Of a roaming network, only where the proof of its station was
            // checked and still holds: what it costs is what the proof says,
            // and a station nobody attested is no place to charge at.
            if (anchor is not null)
            {

                var now      = TimeProvider.GetUtcNow();
                var station  = locationChecks.TryGetValue(location.Id, out var check) ? check.StationOf(evse) : null;

                if (station is null || !station.HoldsAt(anchor, now))
                    return ChargingStart.Refused(ChargingRefusal.Conflict,
                                                 station is null
                                                     ? $"{evse.EVSEId} at {location.Name} was not checked yet: open the station on the map, so that its proof is checked, and charge then."
                                                     : station.Verified
                                                           ? $"The proof of {evse.EVSEId} at {location.Name} was checked at {station.CheckedAt:HH:mm:ss} and does not hold any more: open the station again."
                                                           : $"{evse.EVSEId} at {location.Name} is not signed as the trust anchor of the roaming network requires: {station.Error}");

                var tariff = station.TariffOf(evse.Uid);

                if (tariff is null)
                    return ChargingStart.Refused(ChargingRefusal.Conflict, $"The proof of {evse.EVSEId} at {location.Name} names no price per kWh, and a simulated session counts with nothing else.");

                location  = location with { Tariff = tariff };
                statusOf  = (_, _) => EVSEStatus.Unknown;

            }

            // With an account at an e-mobility provider: a ticket of its own for
            // this start - only where the start can be, so that no ticket is
            // asked for in vain.
            IssuedTicket? ticket = null;

            if (ProviderAccount is not null)
            {

                var now = TimeProvider.GetUtcNow();

                if (Sessions.IsCharging(location.Id, evse.Uid, now))
                    return ChargingStart.Refused(ChargingRefusal.Conflict, $"This phone is already charging at {evse.EVSEId}.");

                if (statusOf(evse, now) is var status && status != EVSEStatus.Available && status != EVSEStatus.Unknown)
                    return ChargingStart.Refused(ChargingRefusal.Conflict, $"{evse.EVSEId} at {location.Name} is {EVSEStatus.InWords(status)}, not available.");

                (ticket, var problem) = await TicketForStartAsync(evse.EVSEId, CancellationToken);

                if (ticket is null)
                {
                    Log.Warning($"'{Who}' wanted to charge at {location.Name}, {evse.EVSEId}, and got no charging ticket: {problem}", "charging", "provider");
                    return ChargingStart.Refused(ChargingRefusal.Provider, problem ?? "The e-mobility provider issued no charging ticket.");
                }

            }

            if (!Sessions.TryStart(location, evse, statusOf, Who, ticket?.Reference, out var session, out var error, out var notSaved))
                return ChargingStart.Refused(notSaved ? ChargingRefusal.NotSaved : ChargingRefusal.Conflict, error);

            Log.Notice($"'{Who}' started charging at {location.Name}, {evse.EVSEId}, with {session.PowerKW:0.#} kW until {session.TargetEnergyKWh:0.#} kWh" +
                       (anchor is not null ? $" for {session.PricePerKWh:0.00##} {session.Currency} per kWh, as its checked proof says" : "") +
                       (ticket is not null ? $", with the charging ticket {ticket.Id} of {ticket.Party}" : "") +
                       $" - simulated: {(ticket is not null ? "the provider takes no start requests yet" : "no e-mobility provider was asked")}. The session is {session.Id}.",
                       "charging", "web");

            return new ChargingStart(session, null, ChargingRefusal.None);

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
