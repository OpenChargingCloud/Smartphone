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

using System.Globalization;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Hermod.DNS;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;

using ChargingEVSE      = cloud.charging.open.Smartphone.Map.ChargingEVSE;
using ChargingLocation  = cloud.charging.open.Smartphone.Map.ChargingLocation;

#endregion

namespace cloud.charging.open.Smartphone.Roaming
{

    /// <summary>
    /// What a roaming network was asked, and what it answered: the text of the
    /// answer, the URL that gave it - or why none did.
    /// </summary>
    public sealed record RoamingNetworkAnswer(String?  Text,
                                              String?  From,
                                              String?  Error);


    /// <summary>
    /// The smartphone asking a roaming network, at the URLs its trust anchor
    /// names: its charging pools, and the proof of a charging station and its
    /// price statements, checked against the trust anchor.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The URLs are asked in their order, the next where one does not answer:
    /// a hub down, a mirror up. None of them is trusted - what they serve is
    /// signed, and what is not signed, the list of pools, is only where to
    /// look. Everything the smartphone says of a station beyond where it is,
    /// its tariffs and its energy meters with their keys, comes from a proof
    /// checked here.
    /// </para>
    /// </remarks>
    public sealed class RoamingNetworkClient
    {

        #region Data

        /// <summary>
        /// How long one URL is waited for.
        /// </summary>
        public static readonly TimeSpan  DefaultTimeout  = TimeSpan.FromSeconds(15);

        private readonly DNSClient  dnsClient;
        private readonly TimeSpan   timeout;

        #endregion

        #region Properties

        /// <summary>
        /// The trust anchor of the roaming network asked.
        /// </summary>
        public RoamingNetworkTrustAnchor  TrustAnchor  { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// Ask the roaming network of the given trust anchor.
        /// </summary>
        /// <param name="TrustAnchor">The trust anchor of the roaming network.</param>
        /// <param name="DNSClient">How to resolve the names of its URLs: the smartphone's own.</param>
        /// <param name="Timeout">How long one URL is waited for.</param>
        public RoamingNetworkClient(RoamingNetworkTrustAnchor  TrustAnchor,
                                    DNSClient                  DNSClient,
                                    TimeSpan?                  Timeout   = null)
        {
            this.TrustAnchor  = TrustAnchor;
            this.dnsClient    = DNSClient;
            this.timeout      = Timeout ?? DefaultTimeout;
        }

        #endregion


        #region GetAsync(PathBelowNetwork, CancellationToken)

        /// <summary>
        /// What the roaming network answers below one of its URLs, the first one
        /// that answers with a success, e.g. "CPs" or "CSs/DE*GEF*S1/proof".
        /// </summary>
        public async Task<RoamingNetworkAnswer> GetAsync(String             PathBelowNetwork,
                                                         CancellationToken  CancellationToken = default)
        {

            var problems = new List<String>();

            foreach (var url in TrustAnchor.URLs)
            {

                var text = url.ToString().TrimEnd('/');

                try
                {

                    var uri     = new Uri(text);
                    var server  = org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.Parse($"{uri.Scheme}://{uri.Authority}");
                    var path    = HTTPPath.Parse(uri.AbsolutePath.TrimEnd('/') + "/" + PathBelowNetwork);

                    await using var http = new HTTPClient(server, DNSClient: dnsClient);

                    // JSON asked for: a hub answers a request that does not
                    // say what it takes with the page of its web interface.
                    var response = await http.GET(path,
                                                  Accept:             AcceptTypes.FromHTTPContentTypes(HTTPContentType.Application.JSON_UTF8),
                                                  RequestTimeout:     timeout,
                                                  CancellationToken:  CancellationToken);

                    if (response.HTTPStatusCode.IsSuccessful)
                        return new RoamingNetworkAnswer(response.HTTPBodyAsUTF8String, text, null);

                    // Not there is an answer, and asking a mirror would only be
                    // told the same.
                    if (response.HTTPStatusCode == HTTPStatusCode.NotFound)
                        return new RoamingNetworkAnswer(null, text, $"{text}/{PathBelowNetwork} is not there.");

                    problems.Add($"{text} answered {response.HTTPStatusCode}" + (response.HTTPBodyAsUTF8String is String { Length: > 0 } body ? $": {(body.Length > 300 ? body[..300] + "..." : body)}" : ""));

                }
                catch (OperationCanceledException) when (CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception e)
                {
                    problems.Add($"{text} could not be reached: {e.Message}");
                }

            }

            return new RoamingNetworkAnswer(null, null, $"The roaming network '{TrustAnchor.Id}' did not answer: {String.Join("; ", problems)}.");

        }

        #endregion


        #region GetLocationsAsync(CancellationToken)

        /// <summary>
        /// The charging pools of the roaming network, as the map shows them:
        /// where they are, who runs them and their EVSEs - nothing of it
        /// signed, only where to look, and without a price, which only a
        /// proof gives.
        /// </summary>
        public async Task<(IReadOnlyList<ChargingLocation>? Locations, String? From, String? Error)> GetLocationsAsync(CancellationToken CancellationToken = default)
        {

            var answer = await GetAsync("CPs", CancellationToken);

            if (answer.Text is null)
                return (null, answer.From, answer.Error);

            JArray pools;

            try
            {
                pools = ParseJSON(answer.Text) as JArray ?? throw new JsonReaderException("It is no JSON array.");
            }
            catch (JsonException e)
            {
                return (null, answer.From, $"What {answer.From}/CPs answered is no list of charging pools: {e.Message}");
            }

            return ([ .. pools.OfType<JObject>().Select(LocationOf).OfType<ChargingLocation>() ], answer.From, null);

        }

        #endregion

        #region (static) LocationOf(Pool)

        /// <summary>
        /// A charging pool as the map shows it, or null where it says nothing of
        /// where it is.
        /// </summary>
        public static ChargingLocation? LocationOf(JObject Pool)
        {

            var id   = Pool["@id"]?.Value<String>();
            var geo  = Pool["geoLocation"] as JObject;

            if (id is null || geo is null ||
                !TryDouble(geo["lat"], out var latitude) || !TryDouble(geo["lng"], out var longitude))
                return null;

            var address  = Pool["address"] as JObject;
            var street   = String.Join(" ", new[] { address?["street"]?.Value<String>(), address?["houseNumber"]?.Value<String>() }.Where(part => !String.IsNullOrWhiteSpace(part)));

            var evses    = (Pool["chargingStations"] as JArray ?? []).OfType<JObject>().
                               SelectMany(station => (station["EVSEs"] as JArray ?? []).OfType<JObject>().
                                                         Select(evse => EVSEOf(evse, station["@id"]?.Value<String>() ?? ""))).
                               OfType<ChargingEVSE>().
                               ToArray();

            return new ChargingLocation(id,
                                        Text(Pool["name"]) ?? id,
                                        street,
                                        address?["postalCode"]?.Value<String>() ?? "",
                                        Text(address?["city"]) ?? "",
                                        address?["country"]?.Value<String>() ?? "",
                                        latitude,
                                        longitude,
                                        Text(Pool["chargingStationOperator"]?["name"]) ?? Pool["chargingStationOperator"]?["@id"]?.Value<String>() ?? "",
                                        null,
                                        evses);

        }

        /// <summary>
        /// An EVSE of a charging station, with its first charging connector.
        /// </summary>
        private static ChargingEVSE? EVSEOf(JObject  EVSE,
                                            String   StationId)
        {

            if (EVSE["@id"]?.Value<String>() is not String id)
                return null;

            var connector    = (EVSE["chargingConnectors"] as JArray ?? []).OfType<JObject>().FirstOrDefault();
            var currentTypes = (EVSE["currentTypes"] as JArray ?? []).Values<String>().ToArray();

            return new ChargingEVSE(id,
                                    id,
                                    connector?["type"]?.  Value<String>() ?? "",
                                    connector?["format"]?.Value<String>() ?? "",
                                    PowerTypeOf(currentTypes, connector?["currentTypes"] as JArray),
                                    TryDecimal(EVSE["maxPower"], out var watt) ? watt / 1000m : 0m,
                                    StationId);

        }

        /// <summary>
        /// The current type of an EVSE in OCPI's words.
        /// </summary>
        private static String PowerTypeOf(String?[]  CurrentTypes,
                                          JArray?    OfConnector)
        {

            var types = OfConnector is not null ? OfConnector.Values<String>().ToArray() : CurrentTypes;

            return types.Contains("DC")             ? "DC"
                 : types.Contains("AC_OnePhase") &&
                  !types.Contains("AC_ThreePhases") ? "AC_1_PHASE"
                 :                                    "AC_3_PHASE";

        }

        #endregion


        #region CheckStationAsync(StationId, At, CancellationToken)

        /// <summary>
        /// The proof of a charging station and its price statements, asked of
        /// the roaming network and checked against its trust anchor at the given
        /// time.
        /// </summary>
        public async Task<StationCheck> CheckStationAsync(String             StationId,
                                                          DateTimeOffset     At,
                                                          CancellationToken  CancellationToken = default)
        {

            var proofAnswer = await GetAsync($"CSs/{StationId}/proof", CancellationToken);

            if (proofAnswer.Text is null)
                return StationCheck.Failed(StationId, At, proofAnswer.Error!, proofAnswer.From);

            if (!RoamingNetworkProof.TryParse(proofAnswer.Text, out var proof, out var parseError))
                return StationCheck.Failed(StationId, At, $"What {proofAnswer.From} gave as the proof of {StationId} is none: {parseError}", proofAnswer.From);

            if (!proof.Verify(TrustAnchor, out var attestation, out var verifyError, At))
                return StationCheck.Failed(StationId, At, $"The proof of {StationId} is not attested by the operator the trust anchor names: {verifyError}", proofAnswer.From, proof);

            // The statements of the pool, the station and its EVSEs. Each is
            // checked on its own: one that is not valid is said and not taken,
            // and leaves the tariffs as the proof gives them.
            var statements       = new List<PriceStatementCheck>();
            var statementsAnswer = await GetAsync($"CSs/{StationId}/priceStatements", CancellationToken);
            var statementsError  = statementsAnswer.Text is null ? statementsAnswer.Error : null;

            if (statementsAnswer.Text is not null)
            {
                try
                {
                    foreach (var json in (ParseJSON(statementsAnswer.Text)["priceStatements"] as JArray ?? []).OfType<JObject>())
                    {

                        if (!RoamingNetworkPriceStatement.TryParse(json, out var statement, out var statementParseError))
                        {
                            statements.Add(new PriceStatementCheck(null, json, false, $"It is no price statement: {statementParseError}"));
                            continue;
                        }

                        statements.Add(statement.TryVerify(proof, out var statementError, At)
                                           ? new PriceStatementCheck(statement, json, true,  null)
                                           : new PriceStatementCheck(statement, json, false, statementError));

                    }
                }
                catch (JsonException e)
                {
                    statementsError = $"What {statementsAnswer.From} gave as the price statements of {StationId} is no JSON: {e.Message}";
                }
            }

            return new StationCheck(StationId,
                                    At,
                                    proofAnswer.From,
                                    proof,
                                    attestation,
                                    null,
                                    proof.TrustAnchorTextMismatch(TrustAnchor),
                                    statements,
                                    statementsError);

        }

        #endregion


        #region (static) ParseJSON(Text)

        /// <summary>
        /// JSON as what is signed in it is read: timestamps as the text they
        /// were signed as, not as dates, and numbers with a fraction as decimals
        /// (WWCP_POI's SPECIFICATION.md, section 1) - a price of 0.65 that came
        /// back as a double would be another price.
        /// </summary>
        public static JToken ParseJSON(String Text)
        {

            using var reader = new JsonTextReader(new StringReader(Text)) {
                                   DateParseHandling   = DateParseHandling.None,
                                   FloatParseHandling  = FloatParseHandling.Decimal
                               };

            return JToken.ReadFrom(reader);

        }

        #endregion

        #region (static) Text(I18N)

        /// <summary>
        /// The text of a multilingual string, in English where it has one, in
        /// German else, or in its first language.
        /// </summary>
        public static String? Text(JToken? I18N)
        {

            if (I18N is JValue value && value.Type == JTokenType.String)
                return value.Value<String>();

            if (I18N is not JObject texts || !texts.HasValues)
                return null;

            return (texts["en"] ?? texts["de"] ?? texts.Properties().First().Value).Value<String>();

        }

        #endregion

        #region (private static) TryDouble(JSON, out Value) / (static) TryDecimal(JSON, out Value)

        private static Boolean TryDouble(JToken? JSON, out Double Value)
        {

            Value = 0;

            if (JSON?.Type is JTokenType.Float or JTokenType.Integer)
            {
                Value = JSON.Value<Double>();
                return Double.IsFinite(Value);
            }

            return JSON?.Type == JTokenType.String &&
                   Double.TryParse(JSON.Value<String>(), NumberStyles.Float, CultureInfo.InvariantCulture, out Value) &&
                   Double.IsFinite(Value);

        }

        public static Boolean TryDecimal(JToken? JSON, out Decimal Value)
        {

            Value = 0;

            if (JSON?.Type is JTokenType.Float or JTokenType.Integer)
            {
                Value = JSON.Value<Decimal>();
                return true;
            }

            return JSON?.Type == JTokenType.String &&
                   Decimal.TryParse(JSON.Value<String>(), NumberStyles.Float, CultureInfo.InvariantCulture, out Value);

        }

        #endregion

    }

}
