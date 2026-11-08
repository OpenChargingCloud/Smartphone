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

using System.Collections.Immutable;

using Newtonsoft.Json.Linq;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// What a charging session at a station of a roaming network keeps, to
    /// check later what it is told of it: the proofs of its station as the
    /// network served them - the one checked before the start, and the
    /// latest one asked for while it charged - and the price statements of
    /// the station that held at the start.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The signed meter values of a session are checked with the key of the
    /// energy meter the station's proof gave at the time each was measured
    /// (WWCP_POI's HistoricalMeterEvidence), and a proof holds only for as
    /// long as its attestations are valid - minutes. The network serves the
    /// proof of its head and of no time before, so the time to ask is while
    /// the session charges: the proof of the start covers its first values,
    /// one asked for every few minutes after covers its last ones. Values in
    /// between, which a charge detail record seldom carries, may be covered
    /// by none.
    /// </para>
    /// <para>
    /// Kept as they came, because what is signed in them is checked against
    /// the text they were signed as.
    /// </para>
    /// </remarks>
    /// <param name="EVSEId">The EVSE, as the roaming network names it.</param>
    /// <param name="StationId">Its charging station.</param>
    /// <param name="Proofs">The proofs of the station: the one of the start first, then those asked for while it charged.</param>
    /// <param name="PriceStatements">The price statements of the station, its pool and its EVSEs that held at the start.</param>
    public sealed record SessionEvidence(String                   EVSEId,
                                         String                   StationId,
                                         ImmutableArray<JObject>  Proofs,
                                         ImmutableArray<JObject>  PriceStatements)
    {

        #region Properties

        /// <summary>
        /// The evidence of the meter of the EVSE and its key, of every proof
        /// kept: what its meter values are checked with.
        /// </summary>
        public HistoricalMeterEvidence MeterEvidence

            => new (EVSEId, Proofs.Select(proof => new HistoricalStationVersion(proof, null, [])));

        /// <summary>
        /// The evidence of the price of the session: the proof of the start
        /// and the price statements that held then.
        /// </summary>
        public SessionPriceEvidence PriceEvidence

            => new (EVSEId, Proofs[0], PriceStatements);

        /// <summary>
        /// Until when the proofs kept hold at most: the latest end of an
        /// attestation in any of them - or null, where none can be read.
        /// </summary>
        public DateTimeOffset? CoveredUntil

            => Proofs.Select(proof => RoamingNetworkProof.TryParse(proof, out var parsed, out _) && !parsed.Attestations.IsEmpty
                                          ? parsed.Attestations.Max(attestation => attestation.ValidUntil)
                                          : (DateTimeOffset?) null).
                      Max();

        /// <summary>
        /// The serial number of the energy meter of the EVSE, as the proof of
        /// the start gives it - or null.
        /// </summary>
        public String? MeterSerialNumber

            => Proofs[0].Descendants().
                         OfType<JObject>().
                         FirstOrDefault(entity => entity["@id"]?.Type == JTokenType.String &&
                                                  entity["@id"]!.Value<String>() == EVSEId &&
                                                  entity["energyMeter"] is JObject)?["energyMeter"]?["serialNumber"]?.Value<String>();

        #endregion


        #region Start(EVSEId, StationId, Proof, PriceStatements)

        /// <summary>
        /// The evidence of a session started now: the proof of its station
        /// checked before, and the price statements that held then.
        /// </summary>
        public static SessionEvidence Start(String                EVSEId,
                                            String                StationId,
                                            JObject               Proof,
                                            IEnumerable<JObject>  PriceStatements)

            => new (EVSEId, StationId, [ Proof ], [ .. PriceStatements ]);

        #endregion

        #region WithLatest(Proof)

        /// <summary>
        /// The evidence with the given proof as the latest one: at most three
        /// are kept - the one of the start, the first one after it, which
        /// covers the first meter values where the one of the start ran out
        /// soon, and the latest, which covers the last ones and takes the
        /// place of the one before.
        /// </summary>
        public SessionEvidence WithLatest(JObject Proof)

            => this with { Proofs = Proofs.Length < MaxProofs
                                        ? Proofs.Add(Proof)
                                        : Proofs.SetItem(MaxProofs - 1, Proof) };

        /// <summary>
        /// How many proofs of its station a session keeps at most.
        /// </summary>
        public const Int32 MaxProofs = 3;

        #endregion


        #region ToJSON() / TryParse(JSON)

        /// <summary>
        /// What the store keeps of it: all of it, as it came.
        /// </summary>
        public JObject ToJSON()

            => new (new JProperty("EVSEId",           EVSEId),
                    new JProperty("stationId",        StationId),
                    new JProperty("proofs",           new JArray(Proofs)),
                    new JProperty("priceStatements",  new JArray(PriceStatements)));

        /// <summary>
        /// The evidence as the store kept it, or null where there is none.
        /// </summary>
        public static SessionEvidence? TryParse(JToken? JSON)
        {

            if (JSON is not JObject json                                                   ||
                json.Value<String>("EVSEId")    is not { Length: > 0 } evseId               ||
                json.Value<String>("stationId") is not { Length: > 0 } stationId            ||
                json["proofs"] is not JArray proofs || proofs.Count == 0 || proofs.Any(proof => proof is not JObject))
                return null;

            return new SessionEvidence(evseId,
                                       stationId,
                                       [ .. proofs.OfType<JObject>() ],
                                       [ .. (json["priceStatements"] as JArray ?? []).OfType<JObject>() ]);

        }

        #endregion

    }

}
