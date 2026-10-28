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
    /// station operator the checked proof gives, of an entity the proof gives,
    /// and valid at the time of the check.
    /// </summary>
    /// <param name="Statement">The statement, where it could be read.</param>
    /// <param name="JSON">The statement as it was served.</param>
    /// <param name="Verified">Whether it holds.</param>
    /// <param name="Error">Why not.</param>
    public sealed record PriceStatementCheck(RoamingNetworkPriceStatement?  Statement,
                                             JObject                        JSON,
                                             Boolean                        Verified,
                                             String?                        Error)
    {

        public JObject ToJSON()

            => new (new JProperty("verified",    Verified),
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
    /// as the trust anchor names it - or why not - and its price statements.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A proof is everything the roaming network says about one charging
    /// station at the head of its history: the station with its EVSEs, their
    /// energy meters with the public keys they sign their measurements with,
    /// and the charging tariffs its EVSEs take, in a hash tree whose root the
    /// operator of the network attests (WWCP_POI's SPECIFICATION.md, section
    /// 8). What it gives completely is attested, and nothing else; so what the
    /// smartphone shows of a station beyond the list of the map is read from
    /// here, and only where the proof holds.
    /// </para>
    /// </remarks>
    public sealed class StationCheck
    {

        #region Properties

        /// <summary>
        /// The charging station checked.
        /// </summary>
        public String                              StationId           { get; }

        /// <summary>
        /// When it was checked: what the attestations and statements had to be
        /// valid at.
        /// </summary>
        public DateTimeOffset                      CheckedAt           { get; }

        /// <summary>
        /// The URL of the roaming network that served the proof.
        /// </summary>
        public String?                             From                { get; }

        /// <summary>
        /// The proof, where one could be read - also where it does not hold.
        /// </summary>
        public RoamingNetworkProof?                Proof               { get; }

        /// <summary>
        /// The attestation the proof holds by, where it does.
        /// </summary>
        public RoamingNetworkAttestation?          Attestation         { get; }

        /// <summary>
        /// Why the proof does not hold, or could not be had.
        /// </summary>
        public String?                             Error               { get; }

        /// <summary>
        /// Where the trust anchor names the network otherwise than the proof
        /// does: a QR code that is not what it says.
        /// </summary>
        public String?                             TextMismatch        { get; }

        /// <summary>
        /// The price statements of the station, its pool and its EVSEs.
        /// </summary>
        public IReadOnlyList<PriceStatementCheck>  PriceStatements     { get; }

        /// <summary>
        /// Why the price statements could not be had.
        /// </summary>
        public String?                             PriceStatementsError  { get; }

        /// <summary>
        /// Whether the proof holds.
        /// </summary>
        public Boolean                             Verified
            => Attestation is not null;

        /// <summary>
        /// The digest of the effective tree of the version the proof is of -
        /// what a start request names as the version the driver saw - where the
        /// proof holds.
        /// </summary>
        public Byte[]?                             EffectiveTree
            => Verified && Proof?.TryCalculateETag(out var etag, out _) == true
                   ? [ .. etag.Digest ]
                   : null;

        #endregion

        #region Constructor(s)

        public StationCheck(String                             StationId,
                            DateTimeOffset                     CheckedAt,
                            String?                            From,
                            RoamingNetworkProof?               Proof,
                            RoamingNetworkAttestation?         Attestation,
                            String?                            Error,
                            String?                            TextMismatch,
                            IReadOnlyList<PriceStatementCheck> PriceStatements,
                            String?                            PriceStatementsError)
        {

            this.StationId             = StationId;
            this.CheckedAt             = CheckedAt;
            this.From                  = From;
            this.Proof                 = Proof;
            this.Attestation           = Attestation;
            this.Error                 = Error;
            this.TextMismatch          = TextMismatch;
            this.PriceStatements       = PriceStatements;
            this.PriceStatementsError  = PriceStatementsError;

        }

        /// <summary>
        /// A check that found no proof that holds.
        /// </summary>
        public static StationCheck Failed(String                StationId,
                                          DateTimeOffset        CheckedAt,
                                          String                Error,
                                          String?               From    = null,
                                          RoamingNetworkProof?  Proof   = null)

            => new (StationId, CheckedAt, From, Proof, null, Error, null, [], null);

        #endregion


        #region HoldsAt(TrustAnchor, Timestamp)

        /// <summary>
        /// Whether the proof still holds at the given time: its attestations
        /// are valid for minutes, so that a proof checked an hour ago says
        /// nothing of now.
        /// </summary>
        public Boolean HoldsAt(RoamingNetworkTrustAnchor  TrustAnchor,
                               DateTimeOffset             Timestamp)

            => Proof is not null &&
               Verified          &&
               Proof.Verify(TrustAnchor, out _, out _, Timestamp);

        #endregion


        #region (private) Operator() / Station()

        /// <summary>
        /// The charging station operator of the station, as the proof gives it.
        /// </summary>
        private JObject? Operator()

            => Proof?.Given(RoamingNetwork.PropertyKeys.ChargingStationOperators).
                      FirstOrDefault(chargingStationOperator => Proof.GivenBelow(chargingStationOperator,
                                                                                 ChargingStationOperator.PropertyKeys.ChargingPools,
                                                                                 ChargingPool.PropertyKeys.ChargingStations).
                                                                      Any(station => IdOf(station) == StationId));

        /// <summary>
        /// The station, as the proof gives it: with its EVSEs, their energy
        /// meters and the properties their templates give them.
        /// </summary>
        private JObject? Station(JObject? Operator)

            => Operator is null || Proof is null
                   ? null
                   : Proof.GivenBelow(Operator,
                                      ChargingStationOperator.PropertyKeys.ChargingPools,
                                      ChargingPool.PropertyKeys.ChargingStations).
                           FirstOrDefault(station => IdOf(station) == StationId);

        #endregion

        #region TariffOf(EVSEId)

        /// <summary>
        /// What charging at the given EVSE of the station costs per kWh, as the
        /// checked proof and the price statements that hold say it: the first
        /// energy price of the first charging tariff the EVSE takes, as a
        /// statement of the EVSE, its station or its pool sets it - in this
        /// order - or as the tariff has it. Null where the proof does not hold,
        /// or names no energy price.
        /// </summary>
        public SmartphoneTariff? TariffOf(String EVSEId)
        {

            if (!Verified)
                return null;

            var chargingStationOperator = Operator();
            var evse                    = EVSEOf(Station(chargingStationOperator), EVSEId);

            if (evse is null)
                return null;

            var tariffs = TariffsOf(chargingStationOperator);

            foreach (var tariffId in (evse["chargingTariffIds"] as JArray ?? []).Values<String>().OfType<String>())
            {

                if (!tariffs.TryGetValue(tariffId, out var tariff))
                    continue;

                var currency = tariff["currency"]?.Value<String>() ?? "";

                if (StatedElements(tariffId, EVSEId) is JArray stated && EnergyPrice(stated) is Decimal statedPrice)
                    return new SmartphoneTariff(statedPrice, currency, tariffId);

                if (EnergyPrice(tariff["elements"] as JArray) is Decimal price)
                    return new SmartphoneTariff(price, currency, tariffId);

            }

            return null;

        }

        #endregion

        #region (private) TariffsOf(Operator) / StatedElements(TariffId, EVSEId)

        private Dictionary<String, JObject> TariffsOf(JObject? Operator)

            => Operator is null || Proof is null
                   ? []
                   : Proof.GivenBelow(Operator, ChargingStationOperator.PropertyKeys.ChargingTariffs).
                           Where (tariff => IdOf(tariff) is not null).
                           GroupBy(tariff => IdOf(tariff)!).
                           ToDictionary(group => group.Key, group => group.First());

        /// <summary>
        /// The elements a price statement that holds sets for the given tariff
        /// at the given EVSE: the statement of the EVSE before the one of its
        /// station before the one of its pool.
        /// </summary>
        private JArray? StatedElements(String  TariffId,
                                       String  EVSEId)
        {

            var holding = PriceStatements.Where(check => check.Verified && check.Statement is not null).
                                          Select(check => check.Statement!).
                                          ToArray();

            var inOrder = holding.Where(statement => statement.IsOf(nameof(EVSE),            EVSEId)).
                          Concat(holding.Where(statement => statement.IsOf(nameof(ChargingStation), StationId))).
                          Concat(holding.Where(statement => statement.EntityType == nameof(ChargingPool)));

            foreach (var statement in inOrder)
                if ((statement.PriceStatusJSON["prices"] as JArray ?? []).OfType<JObject>().
                        FirstOrDefault(prices => prices["chargingTariffId"]?.Value<String>() == TariffId)?["elements"] is JArray elements)
                    return elements;

            return null;

        }

        #endregion

        #region (private static) EnergyPrice(Elements) / EVSEOf(Station, EVSEId) / IdOf(JSON)

        /// <summary>
        /// The first price per kWh of the given tariff elements: of the first
        /// element without restrictions that has one, or else of the first that
        /// has one at all.
        /// </summary>
        private static Decimal? EnergyPrice(JArray? Elements)
        {

            var elements = (Elements ?? []).OfType<JObject>().ToArray();

            foreach (var element in elements.Where(element => element["restrictions"] is null).Concat(elements))
                foreach (var component in (element["priceComponents"] as JArray ?? []).OfType<JObject>())
                    if (component["type"]?.Value<String>() == "ENERGY" && RoamingNetworkClient.TryDecimal(component["price"], out var price))
                        return price;

            return null;

        }

        private static JObject? EVSEOf(JObject?  Station,
                                       String    EVSEId)

            => (Station?[ChargingStation.PropertyKeys.EVSEs] as JArray ?? []).OfType<JObject>().
                   FirstOrDefault(evse => IdOf(evse) == EVSEId);

        private static String? IdOf(JObject JSON)

            => JSON["@id"]?.Value<String>();

        #endregion


        #region ToJSON()

        /// <summary>
        /// The check as a page shows it: whether the proof holds and by whom,
        /// and everything it gives of the station - nothing of it where it does
        /// not hold.
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

            if (!Verified || Proof is null || Attestation is null)
                return json;

            var network                  = Proof.RoamingNetwork;
            var chargingStationOperator  = Operator();
            var station                  = Station(chargingStationOperator);
            var tariffs                  = TariffsOf(chargingStationOperator);

            json.Add(new JProperty("textMismatch",  TextMismatch));

            json.Add(new JProperty("roamingNetwork", new JObject(
                         new JProperty("id",             network["@id"]?.Value<String>()),
                         new JProperty("name",           RoamingNetworkClient.Text(network["name"])),
                         new JProperty("description",    RoamingNetworkClient.Text(network["description"])),
                         new JProperty("operator",       RoamingNetworkClient.Text(network["operator"]?["name"]) ?? network["operator"]?["@id"]?.Value<String>()),
                         new JProperty("revision",       Proof.Revision),
                         new JProperty("commitId",       Proof.CommitId?.ToString()),
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
                         new JProperty("id",    chargingStationOperator?["@id"]?.Value<String>()),
                         new JProperty("name",  RoamingNetworkClient.Text(chargingStationOperator?["name"]))
                     )));

            json.Add(new JProperty("station", new JObject(
                         new JProperty("name",              RoamingNetworkClient.Text(station?["name"])),
                         new JProperty("manufacturer",      ManufacturerName(RoamingNetwork.PropertyKeys.ChargingStationManufacturers, station?["manufacturerId"])),
                         new JProperty("model",             station?["modelId"]?.         Value<String>()),
                         new JProperty("hardwareRevision",  station?["hardwareRevision"]?.Value<String>()),
                         new JProperty("firmwareVersion",   station?["firmwareVersion"]?. Value<String>())
                     )));

            json.Add(new JProperty("evses", new JArray(
                         (station?[ChargingStation.PropertyKeys.EVSEs] as JArray ?? []).OfType<JObject>().Select(evse => {

                             var evseId  = IdOf(evse) ?? "";
                             var meter   = evse["energyMeter"] as JObject;
                             var tariff  = TariffOf(evseId);

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
                                                                           tariffs.TryGetValue(tariffId, out var given)
                                                                               ? new JObject(
                                                                                     new JProperty("id",        tariffId),
                                                                                     new JProperty("name",      RoamingNetworkClient.Text(given["name"])),
                                                                                     new JProperty("currency",  given["currency"]?.Value<String>()),
                                                                                     new JProperty("elements",  given["elements"]?.DeepClone() ?? new JArray())
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

            return json;

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
