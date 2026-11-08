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

using System.Collections.Concurrent;
using System.Globalization;

using Newtonsoft.Json.Linq;

using cloud.charging.open.Smartphone.Charging;
using cloud.charging.open.Smartphone.Map;
using cloud.charging.open.Smartphone.Roaming;

#endregion

namespace cloud.charging.open.Smartphone
{

    public partial class Smartphone
    {

        #region Data

        /// <summary>
        /// How often the proof of the station of a session that charges is
        /// asked for again: a third of how long an attestation is valid by
        /// default, so that the latest proof kept covers the session's end.
        /// </summary>
        public static readonly TimeSpan  ProofRefreshInterval  = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The last check of every session that keeps evidence, with the
        /// session and the configuration it was made of: records, replaced
        /// whenever they change, so that a session that did not change is not
        /// checked again for every page that shows it.
        /// </summary>
        private readonly ConcurrentDictionary<String, (ChargingSession Session, RoamingNetworkConfiguration Configuration, SessionCheck Check)>  sessionChecks  = new();

        private          CancellationTokenSource?  keepingProofs;
        private          Task?                     keepingProofsTask;

        #endregion


        #region SessionJSON(Session, Now)

        /// <summary>
        /// A session as the pages show it, at the given time - at a station of
        /// a roaming network with how its meter values and its price hold.
        /// </summary>
        public JObject SessionJSON(ChargingSession  Session,
                                   DateTimeOffset   Now)
        {

            var json = Session.ToJSON(Now);

            if (CheckOf(Session, Now) is SessionCheck check)
                json.Add(new JProperty("check", check.ToJSON()));

            return json;

        }

        #endregion

        #region CheckOf(Session, Now)

        /// <summary>
        /// The check of a session that keeps evidence: made again where the
        /// session or the roaming network's configuration changed since the
        /// last one - a key no longer trusted checks nothing - else the last.
        /// </summary>
        public SessionCheck? CheckOf(ChargingSession  Session,
                                     DateTimeOffset   Now)
        {

            if (Session.Evidence is null)
                return null;

            var configuration = RoamingNetwork;

            if (sessionChecks.TryGetValue(Session.Id, out var last) &&
                ReferenceEquals(last.Session,       Session) &&
                ReferenceEquals(last.Configuration, configuration))
                return last.Check;

            var check = SessionCheck.Of(configuration, Session, Now);

            if (check is null)
                return null;

            sessionChecks[Session.Id] = (Session, configuration, check);

            // Said once, when its signed meter values are first checked.
            if (check.MeterValues is not null && last.Check?.MeterValues is null)
            {

                if (check.MeterValuesVerified)
                    Log.Notice($"The meter values of the session {Session.Id} at {Session.EVSEId} are signed by its meter {check.MeterValues[0].MeterSerialNumber}, " +
                               $"with the key {check.MeterValues[0].KeyId} the proof of {Session.Evidence.StationId} gave: " +
                               (check.SignedEnergyKWh is Decimal energy
                                    ? $"{check.StartReading!.EnergyKWh?.ToString(CultureInfo.InvariantCulture)} kWh to {check.EndReading!.EnergyKWh?.ToString(CultureInfo.InvariantCulture)} kWh, {energy.ToString(CultureInfo.InvariantCulture)} kWh in all" +
                                      (check.CDREnergyKWh is Decimal reported && Math.Round(reported, 3) != Math.Round(energy, 3)
                                           ? $" - and its charge detail record says {reported.ToString(CultureInfo.InvariantCulture)} kWh"
                                           : "")
                                    : "no begin and end of the transaction among them") + ".",
                               "charging", "meter", "poi");

                else
                    Log.Warning($"The meter values of the session {Session.Id} at {Session.EVSEId} do not hold: " +
                                (check.MeterValuesError ?? check.Error ?? String.Join(" ", check.MeterValues.Where(value => !value.Verified).Select(value => value.Error).Distinct())),
                                "charging", "meter", "poi");

            }

            return check;

        }

        #endregion


        #region (private) EvidenceOf(Station, EVSE)

        /// <summary>
        /// What a session started at the given EVSE of the given checked station
        /// keeps: the proof as the network served it, and the price statements
        /// that held - or nothing, where the proof was not kept as served.
        /// </summary>
        private static SessionEvidence? EvidenceOf(StationCheck  Station,
                                                   ChargingEVSE  EVSE)

            => Station.ProofJSON is JObject proof
                   ? SessionEvidence.Start(EVSE.EVSEId,
                                           Station.StationId,
                                           proof,
                                           Station.PriceStatements.Where(statement => statement.Verified).Select(statement => statement.JSON))
                   : null;

        #endregion

        #region KeepLatestProofAsync(Session, Why, CancellationToken)

        /// <summary>
        /// Ask the roaming network for the proof of the station of the given
        /// session now, and keep it with the session as the latest one, where
        /// it holds: what its meter values of now on are checked with. Said in
        /// the log where it could not be had; never thrown.
        /// </summary>
        /// <param name="Session">The session.</param>
        /// <param name="Why">What it is kept for, for the log.</param>
        /// <param name="CancellationToken">To stop asking.</param>
        public async Task KeepLatestProofAsync(ChargingSession    Session,
                                               String             Why,
                                               CancellationToken  CancellationToken = default)
        {

            if (Session.Evidence is not SessionEvidence evidence || RoamingNetworkClientFor(RoamingNetwork) is not RoamingNetworkClient client)
                return;

            try
            {

                var check = await client.CheckStationAsync(evidence.StationId, TimeProvider.GetUtcNow(), CancellationToken);

                if (!check.Verified || check.ProofJSON is null)
                {
                    Log.Warning($"The proof of {evidence.StationId}, {Why} of the session {Session.Id}, could not be had: {check.Error}", "charging", "poi");
                    return;
                }

                if (!Sessions.TryKeepProof(Session.Id, check.ProofJSON, out var error))
                    Log.Warning($"The proof of {evidence.StationId}, {Why} of the session {Session.Id}, could not be kept: {error}", "charging", "poi");

            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Log.Warning($"The proof of {evidence.StationId}, {Why} of the session {Session.Id}, could not be had: {e.Message}", "charging", "poi");
            }

        }

        #endregion

        #region KeepProofsAsync(CancellationToken)

        /// <summary>
        /// Of every session at a station of a roaming network that still
        /// charges: what the e-mobility provider says of it, so that a session
        /// that ended is known to - and then the proof of its station, where the
        /// proofs kept run out before the next time this is done.
        /// </summary>
        public async Task KeepProofsAsync(CancellationToken CancellationToken = default)
        {

            Boolean Charging(ChargingSession Session)
                => Session.Evidence is not null && Session.IsActiveAt(TimeProvider.GetUtcNow());

            var charging = Sessions.All().Where(Charging).ToArray();

            if (charging.Length == 0)
                return;

            await ReportsFromProviderAsync(charging, CancellationToken);

            foreach (var session in charging.Select(session => Sessions.Get(session.Id)).OfType<ChargingSession>().Where(Charging))
                if (session.Evidence!.CoveredUntil is not DateTimeOffset until || until < TimeProvider.GetUtcNow() + 2 * ProofRefreshInterval)
                    await KeepLatestProofAsync(session, "while it charges", CancellationToken);

        }

        #endregion

        #region (private) StartKeepingProofs() / StopKeepingProofsAsync()

        /// <summary>
        /// Keep the proofs of the stations of sessions that charge, every
        /// <see cref="ProofRefreshInterval"/>, for as long as the phone runs.
        /// </summary>
        private void StartKeepingProofs()
        {

            if (keepingProofs is not null)
                return;

            keepingProofs      = new CancellationTokenSource();
            keepingProofsTask  = KeepingProofsAsync(keepingProofs.Token);

        }

        private async Task KeepingProofsAsync(CancellationToken CancellationToken)
        {

            using var timer = new PeriodicTimer(ProofRefreshInterval, TimeProvider);

            try
            {
                while (await timer.WaitForNextTickAsync(CancellationToken))
                {
                    try
                    {
                        await KeepProofsAsync(CancellationToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        Log.Warning($"The proofs of the stations of the sessions that charge could not be kept: {e.Message}", "charging", "poi");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // The phone stops.
            }

        }

        private async Task StopKeepingProofsAsync()
        {

            if (keepingProofs is null)
                return;

            await keepingProofs.CancelAsync();

            if (keepingProofsTask is not null)
                await keepingProofsTask;

            keepingProofs.Dispose();

            keepingProofs      = null;
            keepingProofsTask  = null;

        }

        #endregion

        #region (protected override) OnStopping()

        /// <summary>
        /// Before the server stops: no more proofs are asked for, and the
        /// status of the roaming network is followed no more.
        /// </summary>
        protected override async Task OnStopping()
        {
            await StopKeepingProofsAsync();
            await StopFollowingRoamingStatusAsync();
            await StopFollowingLiveLinksAsync();
        }

        #endregion

    }

}
