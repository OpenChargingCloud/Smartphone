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
using System.Diagnostics.CodeAnalysis;

using Newtonsoft.Json.Linq;

using cloud.charging.open.Smartphone.Map;
using cloud.charging.open.Smartphone.Roaming;

#endregion

namespace cloud.charging.open.Smartphone
{

    /// <summary>
    /// What the smartphone found when it checked the charging stations of one
    /// charging pool of the map.
    /// </summary>
    /// <param name="Location">The pool, as the map shows it.</param>
    /// <param name="Stations">The check of each of its charging stations.</param>
    public sealed record LocationCheck(ChargingLocation             Location,
                                       IReadOnlyList<StationCheck>  Stations)
    {

        /// <summary>
        /// Whether the proof of every one of its stations holds.
        /// </summary>
        public Boolean Verified
            => Stations.Count > 0 && Stations.All(station => station.Verified);

        /// <summary>
        /// The check of the station of the given EVSE.
        /// </summary>
        public StationCheck? StationOf(ChargingEVSE EVSE)

            => Stations.FirstOrDefault(station => station.StationId == EVSE.StationId);

        public JObject ToJSON()

            => new (new JProperty("locationId",  Location.Id),
                    new JProperty("name",        Location.Name),
                    new JProperty("verified",    Verified),
                    new JProperty("checkedAt",   Stations.Select(station => station.CheckedAt).DefaultIfEmpty().Max().ToString("o")),
                    new JProperty("stations",    new JArray(Stations.Select(station => station.ToJSON()))));

    }


    public partial class Smartphone
    {

        #region Data

        private readonly Object                                   roamingNetworkLock  = new();
        private          RoamingNetworkConfiguration              roamingNetwork;

        /// <summary>
        /// The pools of the map as the roaming network listed them last, when,
        /// from where - or why it did not.
        /// </summary>
        private          (IReadOnlyList<ChargingLocation> Locations, DateTimeOffset At, String? From, String? Error)?  roamingLocations;

        private readonly SemaphoreSlim                            roamingLocationsLoading  = new (1, 1);

        /// <summary>
        /// The last check of every pool that was checked, by its id.
        /// </summary>
        private readonly ConcurrentDictionary<String, LocationCheck>  locationChecks  = new();

        /// <summary>
        /// How long the pools of a roaming network are shown as they were
        /// listed, before they are asked again.
        /// </summary>
        public static readonly TimeSpan  RoamingLocationsMaxAge  = TimeSpan.FromMinutes(1);

        #endregion

        #region Properties

        /// <summary>
        /// The roaming network the map takes its charging stations from.
        /// </summary>
        public RoamingNetworkConfiguration RoamingNetwork
        {
            get
            {
                lock (roamingNetworkLock)
                    return roamingNetwork;
            }
        }

        #endregion


        #region (private) LoadRoamingNetworkConfiguration()

        /// <summary>
        /// The "roamingNetwork" section the configuration file had at the
        /// start - or none, said in the log, where it was not one.
        /// </summary>
        private RoamingNetworkConfiguration LoadRoamingNetworkConfiguration()
        {

            if (RoamingNetworkConfiguration.FromDocument(ConfigurationDocument, out var configuration, out var problem))
                return configuration;

            Log.Warning($"The roaming network's section of the configuration file was not taken, and the map shows its demo stations: {problem}", "map", "poi", "configuration");

            return RoamingNetworkConfiguration.Default;

        }

        #endregion

        #region TryUpdateRoamingNetworkConfiguration(JSON, Who, out Error, out NotSaved)

        /// <summary>
        /// Take the stations of the map from the roaming network of another
        /// trust anchor - or from none, and show the demo stations.
        /// </summary>
        /// <param name="JSON">What the page sent, in the shape of the "roamingNetwork" section.</param>
        /// <param name="Who">Who asked, for the log.</param>
        /// <param name="Error">Why nothing was changed.</param>
        /// <param name="NotSaved">True where the configuration file could not be read or written: nothing about the change was wrong, and nothing was changed.</param>
        public Boolean TryUpdateRoamingNetworkConfiguration(JObject                           JSON,
                                                            String                            Who,
                                                            [NotNullWhen(false)] out String?  Error,
                                                            out Boolean                       NotSaved)
        {

            NotSaved = false;

            if (!RoamingNetworkConfiguration.TryParse(JSON, out var configuration, out Error))
                return false;

            lock (roamingNetworkLock)
            {

                if (!ConfigFile.TryMergeSection(RoamingNetworkConfiguration.SectionName, configuration.ToJSON(), out Error))
                {
                    NotSaved = true;
                    return false;
                }

                roamingNetwork    = configuration;
                roamingLocations  = null;

                // A check holds against the trust anchor it was made with, and
                // no other.
                locationChecks.Clear();

            }

            Log.Notice(configuration.IsSet
                           ? $"'{Who}' at the web interface set the trust anchor of the roaming network '{configuration.TrustAnchor.Id}', at {String.Join(", ", configuration.TrustAnchor.URLs)}: " +
                             $"the map shows its charging stations, each checked against {configuration.TrustAnchor.Keys.Length} identity key(s) of its operator."
                           : $"'{Who}' at the web interface removed the trust anchor of the roaming network: the map shows its demo stations.",
                       "map", "poi", "configuration", "web");

            return true;

        }

        #endregion

        #region RoamingNetworkConfigurationJSON()

        /// <summary>
        /// The roaming network the map takes its stations from, as
        /// Configuration → Map shows it: what its trust anchor says.
        /// </summary>
        public JObject RoamingNetworkConfigurationJSON()
        {

            var anchor = RoamingNetwork.TrustAnchor;
            var json   = anchor?.ToJSON();

            return new JObject(
                       new JProperty("set",            anchor is not null),
                       new JProperty("trustAnchor",    anchor is null ? null : new JObject(
                           new JProperty("roamingNetworkId",  anchor.Id.ToString()),
                           new JProperty("name",              RoamingNetworkClient.Text(json?["name"])),
                           new JProperty("description",       RoamingNetworkClient.Text(json?["description"])),
                           new JProperty("urls",              new JArray(anchor.URLs.Select(url => url.ToString()))),
                           new JProperty("holders",           new JArray(anchor.Holders.Select(keys => new JArray(keys)))),
                           new JProperty("minConsensus",      anchor.MinConsensus),
                           new JProperty("uri",               anchor.ToURL()),
                           new JProperty("qrCode",            anchor.ToQRCode()),
                           new JProperty("json",              json)
                       )),
                       new JProperty("maxTextLength",  RoamingNetworkConfiguration.MaxTextLength),
                       new JProperty("file",           ConfigFile.Path)
                   );

        }

        #endregion


        #region (private) RoamingNetworkClientFor(Configuration)

        private RoamingNetworkClient? RoamingNetworkClientFor(RoamingNetworkConfiguration Configuration)

            => Configuration.IsSet
                   ? new RoamingNetworkClient(Configuration.TrustAnchor, DNSClient)
                   : null;

        #endregion

        #region (private) RoamingLocationsAsync(Refresh, CancellationToken)

        /// <summary>
        /// The pools of the roaming network, as it listed them within the last
        /// minute, or asked again.
        /// </summary>
        private async Task<(IReadOnlyList<ChargingLocation> Locations, DateTimeOffset At, String? From, String? Error)> RoamingLocationsAsync(Boolean            Refresh,
                                                                                                                                              CancellationToken  CancellationToken)
        {

            var configuration  = RoamingNetwork;
            var client         = RoamingNetworkClientFor(configuration);

            if (client is null)
                return ([], TimeProvider.GetUtcNow(), null, null);

            var known = roamingLocations;

            if (!Refresh && known is not null && TimeProvider.GetUtcNow() - known.Value.At < RoamingLocationsMaxAge)
                return known.Value;

            await roamingLocationsLoading.WaitAsync(CancellationToken);

            try
            {

                // Asked meanwhile by whoever held the semaphore.
                known = roamingLocations;

                if (!Refresh && known is not null && TimeProvider.GetUtcNow() - known.Value.At < RoamingLocationsMaxAge)
                    return known.Value;

                var (locations, from, error) = await client.GetLocationsAsync(CancellationToken);
                var now                      = TimeProvider.GetUtcNow();

                (IReadOnlyList<ChargingLocation> Locations, DateTimeOffset At, String? From, String? Error) result;

                if (locations is not null)
                {

                    if (known is null || known.Value.Error is not null || known.Value.Locations.Count != locations.Count)
                        Log.Info($"The roaming network '{client.TrustAnchor.Id}' lists {locations.Count} charging pool(s) with a location, at {from}.", "map", "poi");

                    result = (locations, now, from, null);

                }
                else
                {

                    if (known?.Error != error)
                        Log.Warning(error ?? $"The roaming network '{client.TrustAnchor.Id}' listed no charging pools.", "map", "poi");

                    // What was listed before is still where to look. Not asked
                    // again before a minute is over either: a network that is
                    // down is not asked by every page that opens the map.
                    result = (known?.Locations ?? [], now, known?.From, error);

                }

                lock (roamingNetworkLock)
                {
                    // The trust anchor changed while it was asked: what it
                    // answered belongs to another network.
                    if (ReferenceEquals(roamingNetwork, configuration))
                        roamingLocations = result;
                }

                return result;

            }
            finally
            {
                roamingLocationsLoading.Release();
            }

        }

        #endregion

        #region (private) RoamingLocation(LocationId)

        /// <summary>
        /// A pool of the roaming network as it was listed last.
        /// </summary>
        private ChargingLocation? RoamingLocation(String LocationId)

            => roamingLocations?.Locations.FirstOrDefault(location => location.Id == LocationId);

        #endregion


        #region CheckLocationAsync(LocationId, CancellationToken)

        /// <summary>
        /// Check every charging station of a pool of the map: its proof, asked
        /// of the roaming network and checked against its trust anchor now, and
        /// its price statements.
        /// </summary>
        /// <param name="LocationId">The pool, by the id the map gave it.</param>
        /// <param name="CancellationToken">To stop asking.</param>
        /// <returns>The check; or why there is none - no roaming network is set, or the map has no such pool.</returns>
        public async Task<(LocationCheck? Check, String? Error, Boolean NotFound)> CheckLocationAsync(String             LocationId,
                                                                                                    CancellationToken  CancellationToken = default)
        {

            var configuration  = RoamingNetwork;
            var client         = RoamingNetworkClientFor(configuration);

            if (client is null)
                return (null, "The map shows its demo stations, which nobody signed: there is no roaming network to check them against. Configuration → Map takes its trust anchor.", false);

            var location = (await RoamingLocationsAsync(false, CancellationToken)).Locations.FirstOrDefault(one => one.Id == LocationId);

            if (location is null)
                return (null, $"There is no charging station '{LocationId}' on the map.", true);

            var now       = TimeProvider.GetUtcNow();
            var stations  = location.EVSEs.Select(evse => evse.StationId).OfType<String>().Distinct().ToArray();
            var checks    = await Task.WhenAll(stations.Select(stationId => client.CheckStationAsync(stationId, now, CancellationToken)));
            var check     = new LocationCheck(location, checks);

            foreach (var station in checks)
            {

                if (station.Verified)
                    Log.Info($"The proof of the charging station {station.StationId} of {location.Name} holds: attested by '{station.Attestation!.Signer ?? station.Attestation.KeyId}' " +
                             $"of the roaming network '{client.TrustAnchor.Id}', revision {station.Proof!.Revision}, until {station.Attestation.ValidUntil:o}." +
                             (station.PriceStatements.Any(statement => !statement.Verified)
                                  ? $" {station.PriceStatements.Count(statement => !statement.Verified)} of its price statement(s) do not hold."
                                  : ""),
                             "map", "poi");

                else
                    Log.Warning($"The charging station {station.StationId} of {location.Name} is not shown as signed: {station.Error}", "map", "poi");

            }

            lock (roamingNetworkLock)
            {
                if (ReferenceEquals(roamingNetwork, configuration))
                    locationChecks[LocationId] = check;
            }

            return (check, null, false);

        }

        #endregion

    }

}
