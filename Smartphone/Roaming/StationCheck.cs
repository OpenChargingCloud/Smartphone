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

using cloud.charging.open.protocols.WWCP.POI;

using SmartphoneTariff  = cloud.charging.open.Smartphone.Map.ChargingTariff;

#endregion

namespace cloud.charging.open.Smartphone.Roaming
{

    /// <summary>
    /// One price statement of a charging station, its pool or one of its
    /// EVSEs, and whether it holds: signed by a signing key of the charging
    /// station operator the checked proof gives, of the station, its pool or
    /// one of its EVSEs, naming only tariffs of its entity, and valid at the
    /// time of the check - and whether it is the one in force for its entity.
    /// </summary>
    /// <param name="Statement">The statement, where it could be read.</param>
    /// <param name="JSON">The statement as it was served.</param>
    /// <param name="Verified">Whether it holds.</param>
    /// <param name="Error">Why not.</param>
    /// <param name="Current">Whether it is the statement of its entity in force at the time of the check.</param>
    public sealed record PriceStatementCheck(RoamingNetworkPriceStatement?  Statement,
                                             JObject                        JSON,
                                             Boolean                        Verified,
                                             String?                        Error,
                                             Boolean                        Current   = false)
    {

        public JObject ToJSON()

            => new (new JProperty("verified",    Verified),
                    new JProperty("current",     Current),
                    new JProperty("error",       Error),
                    new JProperty("entityType",  Statement?.EntityType ?? JSON["entityType"]?.Value<String>()),
                    new JProperty("entityId",    Statement?.EntityId   ?? JSON["entityId"]?.  Value<String>()),
                    new JProperty("signer",      Statement?.Signer.ToString()),
                    new JProperty("keyId",       Statement?.KeyId),
                    new JProperty("sequence",    Statement?.Sequence),
                    new JProperty("signedAt",    Statement?.SignedAt.  ToString("o")),
                    new JProperty("validFrom",   Statement?.ValidFrom. ToString("o")),
                    new JProperty("validUntil",  Statement?.ValidUntil?.ToString("o")),
                    new JProperty("prices",      Statement?.PriceStatusJSON["prices"]?.DeepClone() ?? JSON["priceStatus"]?["prices"]?.DeepClone()));

    }


    /// <summary>
    /// What the smartphone found when it checked one charging station of a
    /// roaming network: its proof, attested by the roaming network operator
    /// as the identity keys trusted here require - or why not - and its price
    /// statements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A proof is everything the roaming network says about one charging
    /// station at the head of its history: the station with its EVSEs, their
    /// energy meters with the public keys they sign their measurements with,
    /// and the charging tariffs its EVSEs take, in a hash tree whose root the
    /// operator of the network attests (WWCP_POI's SPECIFICATION.md, section
    /// 8). It is checked against a trust context of the identity keys of the
    /// trust anchor trusted here, evaluated of the key statement the proof
    /// gives at the time of the check (RoamingNetworkTrustContext), and read
    /// as WWCP_POI reads a verified station (VerifiedChargingStation). What it
    /// gives completely is attested, and nothing else; so what the smartphone
    /// shows of a station beyond the list of the map is read from here, and
    /// only where the proof holds.
    /// </para>
    /// </remarks>
    public sealed class StationCheck
    {

        #region Properties

        /// <summary>
        /// The charging station checked.
        /// </summary>
        public String                              StationId             { get; }

        /// <summary>
        /// When it was checked: what the attestations and statements had to be
        /// valid at.
        /// </summary>
        public DateTimeOffset                      CheckedAt             { get; }

        /// <summary>
        /// The URL of the roaming network that served the proof.
        /// </summary>
        public String?                             From                  { get; }

        /// <summary>
        /// The proof, where one could be read - also where it does not hold.
        /// </summary>
        public RoamingNetworkProof?                Proof                 { get; }

        /// <summary>
        /// The proof as the roaming network served it, where it holds: what a
        /// session started here keeps, to check its meter values with later.
        /// </summary>
        public JObject?                            ProofJSON             { get; }

        /// <summary>
        /// The key statement the proof gives, evaluated against the identity
        /// keys trusted here: its signatures, its signers, its quorum and its
        /// validity - where the proof gives one of this network.
        /// </summary>
        public RoamingNetworkTrustEvaluation?      Trust                 { get; }

        /// <summary>
        /// The station as the proof attests it, where it holds.
        /// </summary>
        public VerifiedChargingStation?            Station               { get; }

        /// <summary>
        /// Why the proof does not hold, or could not be had.
        /// </summary>
        public String?                             Error                 { get; }

        /// <summary>
        /// The price statements of the station, its pool and its EVSEs.
        /// </summary>
        public IReadOnlyList<PriceStatementCheck>  PriceStatements       { get; }

        /// <summary>
        /// Why the price statements could not be had.
        /// </summary>
        public String?                             PriceStatementsError  { get; }

        /// <summary>
        /// Why those that hold, each on its own, do not hold together - two of
        /// one sequence of an entity, a split view - so that none is taken, and
        /// no price is known.
        /// </summary>
        public String?                             PriceConflict         { get; }

        /// <summary>
        /// Whether the proof holds.
        /// </summary>
        public Boolean                             Verified
            => Station is not null;

        /// <summary>
        /// What the proof attests and by whom, where it holds.
        /// </summary>
        public RoamingNetworkProofVerification?    Verification
            => Station?.Verification;

        /// <summary>
        /// The first attestation the proof holds by, where it does.
        /// </summary>
        public RoamingNetworkAttestation?          Attestation
            => Station?.Verification.Attestations.FirstOrDefault();

        /// <summary>
        /// Where the trust anchor names the network otherwise than the proof
        /// does: a QR code that is not what it says.
        /// </summary>
        public String?                             TextMismatch
            => Trust?.Context is RoamingNetworkTrustContext context
                   ? Proof?.TrustAnchorTextMismatch(context.TrustAnchor)
                   : null;

        /// <summary>
        /// The digest of the effective tree of the version the proof is of -
        /// what a start request names as the version the driver saw - where the
        /// proof holds.
        /// </summary>
        public Byte[]?                             EffectiveTree
            => Station is not null
                   ? [ .. Station.Verification.ETag.Digest ]
                   : null;

        #endregion

        #region Constructor(s)

        public StationCheck(String                              StationId,
                            DateTimeOffset                      CheckedAt,
                            String?                             From,
                            RoamingNetworkProof?                Proof,
                            RoamingNetworkTrustEvaluation?      Trust,
                            VerifiedChargingStation?            Station,
                            String?                             Error,
                            IReadOnlyList<PriceStatementCheck>  PriceStatements,
                            String?                             PriceStatementsError,
                            String?                             PriceConflict,
                            JObject?                            ProofJSON   = null)
        {

            this.StationId             = StationId;
            this.CheckedAt             = CheckedAt;
            this.From                  = From;
            this.Proof                 = Proof;
            this.ProofJSON             = ProofJSON;
            this.Trust                 = Trust;
            this.Station               = Station;
            this.Error                 = Error;
            this.PriceStatements       = PriceStatements;
            this.PriceStatementsError  = PriceStatementsError;
            this.PriceConflict         = PriceConflict;

        }

        /// <summary>
        /// A check that found no proof that holds.
        /// </summary>
        public static StationCheck Failed(String                          StationId,
                                          DateTimeOffset                  CheckedAt,
                                          String                          Error,
                                          String?                         From    = null,
                                          RoamingNetworkProof?            Proof   = null,
                                          RoamingNetworkTrustEvaluation?  Trust   = null)

            => new (StationId, CheckedAt, From, Proof, Trust, null, Error, [], null, null);

        #endregion


        #region HoldsAt(Configuration, Timestamp)

        /// <summary>
        /// Whether the proof still holds at the given time, against the identity
        /// keys trusted then: its attestations are valid for minutes, so that a
        /// proof checked an hour ago says nothing of now, and a trust context is
        /// one of a single time.
        /// </summary>
        public Boolean HoldsAt(RoamingNetworkConfiguration  Configuration,
                               DateTimeOffset               Timestamp)

            => Verified          &&
               Proof is not null &&
               Configuration.Evaluate(Proof, Timestamp, out _)?.Context is RoamingNetworkTrustContext context &&
               VerifiedChargingStation.TryVerify(context, Proof, out _, out _, StationId);

        #endregion


        #region TariffOf(EVSEId)

        /// <summary>
        /// What charging at the given EVSE of the station costs per kWh, as the
        /// checked proof and the price statements in force say it: the first
        /// energy price of the first charging tariff the EVSE takes, as a
        /// statement of the EVSE, its station or its pool sets it - in this
        /// order - or as the tariff has it. Null where the proof does not hold,
        /// names no energy price, or where its statements contradict each other.
        /// </summary>
        public SmartphoneTariff? TariffOf(String EVSEId)
        {

            if (Station is null || PriceConflict is not null || !Station.Tariffs.TryGetValue(EVSEId, out var tariffs))
                return null;

            foreach (var tariff in tariffs)
            {

                var tariffId  = IdOf(tariff) ?? "";
                var currency  = tariff["currency"]?.Value<String>() ?? "";

                if (StatedElements(tariffId, EVSEId) is JArray stated && EnergyPrice(stated) is Decimal statedPrice)
                    return new SmartphoneTariff(statedPrice, currency, tariffId);

                if (EnergyPrice(tariff["elements"] as JArray) is Decimal price)
                    return new SmartphoneTariff(price, currency, tariffId);

            }

            return null;

        }

        #endregion

        #region (private) StatedElements(TariffId, EVSEId)

        /// <summary>
        /// The elements the price statement in force sets for the given tariff
        /// at the given EVSE: the statement of the EVSE before the one of its
        /// station before the one of its pool.
        /// </summary>
        private JArray? StatedElements(String  TariffId,
                                       String  EVSEId)
        {

            var current = PriceStatements.Where (check => check.Current && check.Statement is not null).
                                          Select(check => check.Statement!).
                                          ToArray();

            var inOrder = current.Where(statement => statement.IsOf(nameof(EVSE),            EVSEId)).
                          Concat(current.Where(statement => statement.IsOf(nameof(ChargingStation), StationId))).
                          Concat(current.Where(statement => statement.IsOf(nameof(ChargingPool),    IdOf(Station!.ChargingPool) ?? "")));

            foreach (var statement in inOrder)
                if ((statement.PriceStatusJSON["prices"] as JArray ?? []).OfType<JObject>().
                        FirstOrDefault(prices => prices["chargingTariffId"]?.Value<String>() == TariffId)?["elements"] is JArray elements)
                    return elements;

            return null;

        }

        #endregion

        #region (private static) EnergyPrice(Elements) / IdOf(JSON)

        /// <summary>
        /// The first price per kWh of the given tariff elements: of the first
        /// element without restrictions that has one, or else of the first that
        /// has one at all.
        /// </summary>
        internal static Decimal? EnergyPrice(JArray? Elements)
        {

            var elements = (Elements ?? []).OfType<JObject>().ToArray();

            foreach (var element in elements.Where(element => element["restrictions"] is null).Concat(elements))
                foreach (var component in (element["priceComponents"] as JArray ?? []).OfType<JObject>())
                    if (component["type"]?.Value<String>() == "ENERGY" && RoamingNetworkClient.TryDecimal(component["price"], out var price))
                        return price;

            return null;

        }

        private static String? IdOf(JObject JSON)

            => JSON["@id"]?.Value<String>();

        #endregion


        #region ToJSON()

        /// <summary>
        /// The check as a page shows it: whether the proof holds and by whom,
        /// how its key statement fared against the identity keys trusted here,
        /// and everything the proof gives of the station - nothing of it where
        /// it does not hold.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("stationId",  StationId),
                           new JProperty("checkedAt",  CheckedAt.ToString("o")),
                           new JProperty("from",       From),
                           new JProperty("verified",   Verified),
                           new JProperty("error",      Error)
                       );

            if (Trust is not null)
                json.Add(new JProperty("trust", new JObject(
                             new JProperty("signaturesValid",    Trust.SignaturesValid),
                             new JProperty("signersTrusted",     Trust.SignersTrusted),
                             new JProperty("quorumReached",      Trust.QuorumReached),
                             new JProperty("withinValidity",     Trust.WithinValidity),
                             new JProperty("trustedHolders",     Trust.TrustedHolderCount),
                             new JProperty("requiredConsensus",  Trust.RequiredConsensus),
                             new JProperty("attestedBy",         new JArray(Verification is null ? [] : Verification.AttestedBy.ToArray())),
                             new JProperty("validUntil",         Verification?.ValidUntil.ToString("o"))
                         )));

            if (Station is null || Proof is null || Attestation is null)
                return json;

            var network  = Proof.RoamingNetwork;
            var station  = Station.ChargingStation;

            json.Add(new JProperty("textMismatch",  TextMismatch));
            json.Add(new JProperty("warnings",      new JArray(Station.Warnings)));

            json.Add(new JProperty("roamingNetwork", new JObject(
                         new JProperty("id",             network["@id"]?.Value<String>()),
                         new JProperty("name",           RoamingNetworkClient.Text(network["name"])),
                         new JProperty("description",    RoamingNetworkClient.Text(network["description"])),
                         new JProperty("operator",       RoamingNetworkClient.Text(network["operator"]?["name"]) ?? network["operator"]?["@id"]?.Value<String>()),
                         new JProperty("revision",       Station.Revision),
                         new JProperty("commitId",       Station.CommitId?.ToString()),
                         new JProperty("effectiveTree",  EffectiveTree is Byte[] effectiveTree ? Convert.ToHexStringLower(effectiveTree) : null)
                     )));

            json.Add(new JProperty("attestation", new JObject(
                         new JProperty("role",        Attestation.Role),
                         new JProperty("signer",      Attestation.Signer),
                         new JProperty("algorithm",   Attestation.Algorithm),
                         new JProperty("keyId",       Attestation.KeyId),
                         new JProperty("signedAt",    Attestation.SignedAt.  ToString("o")),
                         new JProperty("validUntil",  Attestation.ValidUntil.ToString("o"))
                     )));

            json.Add(new JProperty("chargingStationOperator", new JObject(
                         new JProperty("id",    Station.Operator["@id"]?.Value<String>()),
                         new JProperty("name",  RoamingNetworkClient.Text(Station.Operator["name"]))
                     )));

            json.Add(new JProperty("station", new JObject(
                         new JProperty("name",              RoamingNetworkClient.Text(station["name"])),
                         new JProperty("manufacturer",      ManufacturerName(RoamingNetwork.PropertyKeys.ChargingStationManufacturers, station["manufacturerId"])),
                         new JProperty("model",             station["modelId"]?.         Value<String>()),
                         new JProperty("hardwareRevision",  station["hardwareRevision"]?.Value<String>()),
                         new JProperty("firmwareVersion",   station["firmwareVersion"]?. Value<String>())
                     )));

            json.Add(new JProperty("webPayment",  WebPaymentJSON(Station.ChargingStation, Station.ChargingPool, Station.Operator)));

            json.Add(new JProperty("evses", new JArray(
                         Station.EVSEs.Select(evse => {

                             var evseId  = IdOf(evse) ?? "";
                             var meter   = evse["energyMeter"] as JObject;
                             var tariff  = TariffOf(evseId);
                             var given   = Station.Tariffs.TryGetValue(evseId, out var tariffs) ? tariffs : [];

                             return new JObject(
                                        new JProperty("id",            evseId),
                                        new JProperty("maxPowerKW",    RoamingNetworkClient.TryDecimal(evse["maxPower"], out var watt) ? watt / 1000m : null),
                                        new JProperty("currentTypes",  evse["currentTypes"]?.DeepClone() ?? new JArray()),
                                        new JProperty("connectors",    new JArray((evse["chargingConnectors"] as JArray ?? []).OfType<JObject>().Select(connector => new JObject(
                                                                           new JProperty("id",      connector["@id"]?.   Value<String>()),
                                                                           new JProperty("type",    connector["type"]?.  Value<String>()),
                                                                           new JProperty("format",  connector["format"]?.Value<String>())
                                                                       )))),
                                        new JProperty("tariffs",       new JArray((evse["chargingTariffIds"] as JArray ?? []).Values<String>().OfType<String>().Select(tariffId =>
                                                                           given.FirstOrDefault(tariff => IdOf(tariff) == tariffId) is JObject found
                                                                               ? new JObject(
                                                                                     new JProperty("id",        tariffId),
                                                                                     new JProperty("name",      RoamingNetworkClient.Text(found["name"])),
                                                                                     new JProperty("currency",  found["currency"]?.Value<String>()),
                                                                                     new JProperty("elements",  found["elements"]?.DeepClone() ?? new JArray())
                                                                                 )
                                                                               : new JObject(
                                                                                     new JProperty("id",        tariffId),
                                                                                     new JProperty("missing",   true)
                                                                                 )))),
                                        new JProperty("price",         tariff is null
                                                                           ? null
                                                                           : new JObject(
                                                                                 new JProperty("pricePerKWh",  tariff.PricePerKWh),
                                                                                 new JProperty("currency",     tariff.Currency)
                                                                             )),
                                        new JProperty("energyMeter",   meter is null ? null : MeterJSON(meter))
                                    );

                         }))));

            json.Add(new JProperty("priceStatements",       new JArray(PriceStatements.Select(statement => statement.ToJSON()))));
            json.Add(new JProperty("priceStatementsError",  PriceStatementsError));
            json.Add(new JProperty("priceConflict",         PriceConflict));

            return json;

        }

        #endregion

        #region (static) WebPaymentJSON(Station, Pool, Operator)

        /// <summary>
        /// The web payment page of the operator of a verified station, where a
        /// driver pays for charging there ad hoc and starts it: the station's
        /// own, else that of its pool, else the operator's - each as the proof
        /// gives it, of the station and its pool as they are at runtime, with
        /// what their templates give them. Null where none is given.
        /// </summary>
        /// <remarks>
        /// Not the URL in the dynamic QR code of the station, which is of one
        /// EVSE and changes every few minutes; this one is static, so it shows
        /// nothing of where the driver stands. Only an https URL is offered:
        /// a page that takes payments over anything else, or a URL that is no
        /// web page at all, is said and not linked - the first one given
        /// decides, so a station's own is never passed over for its pool's.
        /// </remarks>
        /// <param name="Station">The station as the proof gives it.</param>
        /// <param name="Pool">Its pool as the proof gives it.</param>
        /// <param name="Operator">Its operator as the proof gives it.</param>
        public static JObject? WebPaymentJSON(JObject  Station,
                                              JObject  Pool,
                                              JObject  Operator)
        {

            if (WebPaymentOf(Station, Pool, Operator) is not (String text, String of))
                return null;

            var https = Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
                        uri.Scheme == Uri.UriSchemeHttps;

            return new JObject(
                       new JProperty("url",    https ? text : null),
                       new JProperty("of",     of),
                       new JProperty("error",  https ? null : $"'{text}' is no https URL, so it is not offered.")
                   );

        }

        #endregion

        #region WebPaymentURL / (private static) WebPaymentOf(Station, Pool, Operator)

        /// <summary>
        /// The web payment page of the operator of the station, where its proof
        /// holds - the station's own, else that of its pool, else the
        /// operator's -, as the proof gives it: what a hand-over of the page is
        /// taken from (<see cref="Charging.WebPaymentHandOver"/>). Null where
        /// the proof gives none.
        /// </summary>
        public String? WebPaymentURL
            => Station is VerifiedChargingStation station
                   ? WebPaymentOf(station.ChargingStation, station.ChargingPool, station.Operator)?.URL
                   : null;

        /// <summary>
        /// The first web payment page given - of the station, its pool, its
        /// operator - and whose it is, or null where none is given.
        /// </summary>
        private static (String URL, String Of)? WebPaymentOf(JObject  Station,
                                                             JObject  Pool,
                                                             JObject  Operator)
        {

            foreach (var (entity, of) in new (JObject Entity, String Of)[] {
                                             (Station,   "chargingStation"),
                                             (Pool,      "chargingPool"),
                                             (Operator,  "chargingStationOperator")
                                         })
            {

                // "webPaymentURL" of a station, a pool and an operator alike.
                if (entity[ChargingStationOperator.PropertyKeys.WebPaymentURL] is JValue { Type: JTokenType.String } value &&
                    value.Value<String>() is String text)
                {
                    return (text, of);
                }

            }

            return null;

        }

        #endregion

        #region (private) MeterJSON(Meter) / ManufacturerName(ChildArray, Id)

        /// <summary>
        /// An energy meter as the proof gives it: who made it, its serial number,
        /// the formats it signs its measurements in, and the public keys it
        /// signs them with - what a transparency software checks the meter
        /// values of a session with.
        /// </summary>
        private JObject MeterJSON(JObject Meter)

            => new (new JProperty("id",                Meter["@id"]?.         Value<String>()),
                    new JProperty("manufacturer",      ManufacturerName(RoamingNetwork.PropertyKeys.EnergyMeterManufacturers, Meter["manufacturerId"])),
                    new JProperty("model",             Meter["modelId"]?.     Value<String>()),
                    new JProperty("serialNumber",      Meter["serialNumber"]?.Value<String>()),
                    new JProperty("firmwareVersion",   Meter["firmwareVersion"]?.Value<String>()),
                    new JProperty("signatureMethods",  Meter["signatureMethods"]?.DeepClone() ?? new JArray()),
                    new JProperty("publicKeys",        new JArray((Meter["publicKeys"] as JArray ?? []).OfType<JObject>().Select(key =>
                                                           MeasurementPublicKey.TryParse(key, out var publicKey, out var error)
                                                               ? new JObject(
                                                                     new JProperty("id",                publicKey.Id),
                                                                     new JProperty("algorithm",         publicKey.Algorithm),
                                                                     new JProperty("key",               Convert.ToBase64String([ .. publicKey.Key ])),
                                                                     new JProperty("certificateChain",  publicKey.CertificateChain.Length)
                                                                 )
                                                               : new JObject(
                                                                     new JProperty("id",                key["@id"]?.Value<String>()),
                                                                     new JProperty("key",               key["key"]?.Value<String>()),
                                                                     new JProperty("error",             error)
                                                                 )))));

        private String? ManufacturerName(String   ChildArray,
                                         JToken?  Id)

            => Id?.Value<String>() is not String id
                   ? null
                   : RoamingNetworkClient.Text(Proof?.Given(ChildArray).FirstOrDefault(manufacturer => IdOf(manufacturer) == id)?["name"]) ?? id;

        #endregion

    }

}
