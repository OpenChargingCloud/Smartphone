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
        /// <summary>The e-mobility provider issued no charging ticket for it, started or stopped nothing, or could not be reached.</summary>
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


    /// <summary>
    /// What came of "Stop": the session stopped, or why it was not.
    /// </summary>
    public sealed record ChargingStop(ChargingSession?  Session,
                                      String?           Error,
                                      ChargingRefusal   Refusal)
    {

        public static ChargingStop Refused(ChargingRefusal Refusal, String Error)
            => new (null, Error, Refusal);

    }


    public partial class Smartphone
    {

        #region StartChargingAsync(LocationId, EVSEUid, Who, CancellationToken = default)

        /// <summary>
        /// Start charging at an EVSE of a station of the map. At a station of a
        /// roaming network, only once the proof of its station was checked and
        /// while it holds, for the price it gives. Without an account at an
        /// e-mobility provider, simulated. With one, through it: a charging
        /// ticket of its own, asked for right before the start, and a start
        /// request signed by the ticket's key, which the provider countersigns
        /// and sends the charge point operator - the session as the provider
        /// reports it from then on, simulated for as long as it reports nothing.
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

            // What the driver saw, for the start request to name.
            Byte[]?  effectiveTree  = null;
            String?  tariffId       = null;

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

                location       = location with { Tariff = tariff };
                statusOf       = (_, _) => EVSEStatus.Unknown;
                effectiveTree  = station.EffectiveTree;
                tariffId       = tariff.TariffId;

            }

            // With an account at an e-mobility provider: a ticket of its own for
            // this start - only where the start can be, so that no ticket is
            // asked for in vain - and the start through the provider.
            IssuedTicket?            ticket  = null;
            ChargingStartReference?  start   = null;

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

                (start, problem) = await StartThroughProviderAsync(ticket, evse.EVSEId, effectiveTree, tariffId, CancellationToken);

                if (start is null)
                {
                    Log.Warning($"'{Who}' wanted to charge at {location.Name}, {evse.EVSEId}, and {ticket.Party} started nothing: {problem}", "charging", "provider");
                    return ChargingStart.Refused(ChargingRefusal.Provider, problem ?? "The e-mobility provider started nothing.");
                }

            }

            if (!Sessions.TryStart(location, evse, statusOf, Who, ticket?.Reference, start, out var session, out var error, out var notSaved))
            {

                // Started at the provider, and no session here to stop it with:
                // stopped again, rather than left charging for nobody.
                if (ticket is not null && start is not null)
                {

                    var (stopped, stopProblem) = await StopThroughProviderAsync(ticket.Reference, start, CancellationToken);

                    Log.Warning($"{ticket.Party} started charging at {evse.EVSEId} for '{Who}', and this phone could not keep the session: {error} " +
                                (stopped is not null ? "It was stopped again." : $"It could not be stopped again: {stopProblem}"),
                                "charging", "provider");

                }

                return ChargingStart.Refused(notSaved ? ChargingRefusal.NotSaved : ChargingRefusal.Conflict, error);

            }

            Log.Notice($"'{Who}' started charging at {location.Name}, {evse.EVSEId}, with {session.PowerKW:0.#} kW until {session.TargetEnergyKWh:0.#} kWh" +
                       (anchor is not null ? $" for {session.PricePerKWh:0.00##} {session.Currency} per kWh, as its checked proof says" : "") +
                       (ticket is not null && start is not null
                            ? $", through {ticket.Party} with the charging ticket {ticket.Id} and the token {start.Token} - as {ticket.Party} reports it, simulated until it does"
                            :  " - simulated: no e-mobility provider was asked") +
                       $". The session is {session.Id}.",
                       "charging", "web");

            return new ChargingStart(session, null, ChargingRefusal.None);

        }

        #endregion

        #region StopChargingAsync(Id, Who, CancellationToken = default)

        /// <summary>
        /// Stop a charging session that is still charging - one started through
        /// the e-mobility provider through it, with a stop request signed by the
        /// ticket's key, and only once the provider countersigned it.
        /// </summary>
        /// <param name="Id">The session.</param>
        /// <param name="Who">Who asked, for the log and the session.</param>
        /// <param name="CancellationToken">To stop asking the provider.</param>
        public async Task<ChargingStop> StopChargingAsync(String             Id,
                                                          String             Who,
                                                          CancellationToken  CancellationToken = default)
        {

            var running = Sessions.Get(Id);

            if (running is null)
                return ChargingStop.Refused(ChargingRefusal.NotFound, $"There is no charging session '{Id}' on this phone.");

            if (!running.IsActiveAt(TimeProvider.GetUtcNow()))
                return ChargingStop.Refused(ChargingRefusal.Conflict, $"The charging session '{Id}' is {running.StatusAt(TimeProvider.GetUtcNow()).ToString().ToLowerInvariant()} already.");

            ChargingStartReference? stopped = null;

            if (running.Ticket is not null && running.Start is not null)
            {

                (stopped, var problem) = await StopThroughProviderAsync(running.Ticket, running.Start, CancellationToken);

                if (stopped is null)
                {
                    Log.Warning($"'{Who}' wanted to stop charging at {running.EVSEId} ({running.Id}), and {running.Ticket.Party} stopped nothing: {problem}", "charging", "provider");
                    return ChargingStop.Refused(ChargingRefusal.Provider, problem ?? "The e-mobility provider stopped nothing.");
                }

            }

            if (!Sessions.TryStop(Id, Who, stopped, out var session, out var error, out var notFound, out var notSaved))
                return ChargingStop.Refused(notFound ? ChargingRefusal.NotFound
                                          : notSaved ? ChargingRefusal.NotSaved
                                          :            ChargingRefusal.Conflict,
                                            stopped is not null
                                                ? $"{running.Ticket!.Party} stopped the session, and this phone could not say so: {error}"
                                                : error);

            Log.Notice($"'{Who}' stopped charging at {session.LocationName}, {session.EVSEId}, after {session.EnergyAt(TimeProvider.GetUtcNow()):0.###} kWh" +
                       (stopped is not null ? $", through {running.Ticket!.Party}, which countersigned the stop of the token {stopped.Token}" : "") +
                       $". The session was {session.Id}.",
                       "charging", "web");

            return new ChargingStop(session, null, ChargingRefusal.None);

        }

        #endregion

        #region SessionsJSON(OnlyActive)

        /// <summary>
        /// The charging sessions of this phone, the newest first - or only the
        /// ones still starting or charging. Simulated, where one of them is,
        /// or where none is shown and a start now would be.
        /// </summary>
        public JObject SessionsJSON(Boolean OnlyActive = false)
        {

            var now     = TimeProvider.GetUtcNow();
            var all     = Sessions.All();
            var shown   = OnlyActive ? all.Where(session => session.IsActiveAt(now)).ToArray() : all;

            return new JObject(
                       new JProperty("timestamp",  now.ToString("o")),
                       new JProperty("simulated",  shown.Count > 0
                                                       ? shown.Any(session => session.IsSimulated)
                                                       : ProviderAccount is null),
                       new JProperty("active",     all.Count(session => session.IsActiveAt(now))),
                       new JProperty("total",      all.Count),
                       new JProperty("sessions",   new JArray(shown.Select(session => session.ToJSON(now))))
                   );

        }

        #endregion

    }

}
