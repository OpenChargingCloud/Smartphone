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

using System.Security.Cryptography;

using Newtonsoft.Json.Linq;

using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.X509;

using org.GraphDefined.Vanaheimr.Illias;
using org.GraphDefined.Vanaheimr.Hermod;
using org.GraphDefined.Vanaheimr.Hermod.HTTP;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// A roaming network served over HTTP on the loopback, as a hub would:
    /// its operator OCC stating its identity key and certifying its signing
    /// key, attesting its head, and one charging station operator, DE*GEF,
    /// with one pool of an AC and a DC station, each EVSE with its energy
    /// meter and the public key it signs its measurements with, and a price
    /// statement of the DC EVSE.
    /// </summary>
    /// <remarks>
    /// The keys are new for every network: a test that verifies against the
    /// trust anchor of one network and is shown the proof of another sees
    /// what an app would, a proof nobody it trusts attested.
    /// </remarks>
    public sealed class TestRoamingNetwork : IAsyncDisposable
    {

        #region Data

        public static readonly RoamingNetwork_Id           NetworkId   = RoamingNetwork_Id.         Parse("QA");
        public static readonly RoamingNetworkOperator_Id   RNOId       = RoamingNetworkOperator_Id. Parse("OCC");
        public static readonly ChargingStationOperator_Id  GEF         = ChargingStationOperator_Id.Parse("DE*GEF");

        public const String  PoolId      = "DE*GEF*P1";
        public const String  ACStation   = "DE*GEF*S1";
        public const String  DCStation   = "DE*GEF*S2";
        public const String  ACEVSE      = "DE*GEF*E1*1";
        public const String  DCEVSE      = "DE*GEF*E2*1";
        public const String  ACTariff    = "DE*GEF*T_AC";
        public const String  DCTariff    = "DE*GEF*T_DC";

        #endregion

        #region Properties

        public HTTPServer                 HTTPServer        { get; }
        public POI_API                    API               { get; }
        public RoamingNetworkHistory      History           { get; }

        /// <summary>
        /// What the operator prints as a QR code: the network, the URL below
        /// which it is served here, and the thumbprint of its identity key.
        /// </summary>
        public RoamingNetworkTrustAnchor  TrustAnchor       { get; }

        /// <summary>
        /// The public keys, DER, the energy meters of the AC and the DC EVSE
        /// sign their measurements with, as Base64.
        /// </summary>
        public String                     ACMeterKey        { get; }
        public String                     DCMeterKey        { get; }

        public String                     URL
            => TrustAnchor.URLs[0].ToString();

        #endregion

        #region Constructor(s)

        private TestRoamingNetwork(HTTPServer                 HTTPServer,
                                   POI_API                    API,
                                   RoamingNetworkHistory      History,
                                   RoamingNetworkTrustAnchor  TrustAnchor,
                                   String                     ACMeterKey,
                                   String                     DCMeterKey)
        {
            this.HTTPServer   = HTTPServer;
            this.API          = API;
            this.History      = History;
            this.TrustAnchor  = TrustAnchor;
            this.ACMeterKey   = ACMeterKey;
            this.DCMeterKey   = DCMeterKey;
        }

        #endregion


        #region (static) Start(Now = null)

        /// <summary>
        /// The network, attested now, served on a free port of the loopback.
        /// </summary>
        public static async Task<TestRoamingNetwork> Start(DateTimeOffset? Now = null)
        {

            var now          = Now ?? DateTimeOffset.UtcNow;
            var created      = now.AddDays(-1);

            var rnoIdentity  = NewKey("secp521r1");
            var rnoSigning   = NewKey("secp256r1");
            var gefIdentity  = NewKey("secp521r1");
            var gefSigning   = NewKey("secp256r1");
            var acMeter      = NewKey("secp256r1");
            var dcMeter      = NewKey("secp256r1");

            var rnoIdentityKey  = POIPublicKey.From(rnoIdentity);
            var rnoSigningKey   = POIPublicKey.From(rnoSigning);
            var gefSigningKey   = POIPublicKey.From(gefSigning);

            var keyStatement    = RoamingNetworkKeyStatement.Create(rnoIdentity, COSEAlgorithm.ES512, 1, NetworkId, RNOId, [ rnoIdentityKey ], created, now.AddYears(1));
            var certificate     = RoamingNetworkSigningKeyCertificate.Create(rnoIdentity, COSEAlgorithm.ES512, RNOId, rnoSigningKey, created);

            var acMeterKey      = DER(acMeter);
            var dcMeterKey      = DER(dcMeter);

            var json = new JObject(
                           new JProperty("@id",          NetworkId.ToString()),
                           new JProperty("name",         new JObject(new JProperty("en", "Quality Assurance"))),
                           new JProperty("description",  new JObject(new JProperty("en", "The roaming network of the tests of the smartphone."))),
                           new JProperty("created",      created.ToISO8601()),
                           new JProperty("operator",     new JObject(
                               new JProperty("@id",                     RNOId.ToString()),
                               new JProperty("name",                    new JObject(new JProperty("en", "Open Charging Cloud"))),
                               new JProperty("created",                 created.ToISO8601()),
                               new JProperty("identityKeys",            new JArray(rnoIdentityKey.ToJSON())),
                               new JProperty("signingKeyCertificates",  new JArray(certificate.ToJSON(Embedded: true))),
                               new JProperty("keyStatement",            keyStatement.ToJSON(Embedded: true))
                           )),
                           new JProperty("chargingStationOperators", new JArray(new JObject(
                               new JProperty("@id",           GEF.ToString()),
                               new JProperty("name",          new JObject(new JProperty("en", "GraphDefined Charging"))),
                               new JProperty("created",       created.ToISO8601()),
                               new JProperty("identityKeys",  new JArray(POIPublicKey.From(gefIdentity).ToJSON())),
                               new JProperty("signingKeys",   new JArray(gefSigningKey.ToJSON())),
                               new JProperty("chargingTariffs", new JArray(
                                   Tariff(ACTariff, "AC", 0.49m, created),
                                   Tariff(DCTariff, "DC", 0.69m, created)
                               )),
                               new JProperty("chargingPools", new JArray(new JObject(
                                   new JProperty("@id",          PoolId),
                                   new JProperty("name",         new JObject(new JProperty("en", "Jena, Paradiesbahnhof"))),
                                   new JProperty("address",      new JObject(
                                       new JProperty("street",       "Am Paradiesbahnhof"),
                                       new JProperty("houseNumber",  "1"),
                                       new JProperty("postalCode",   "07743"),
                                       new JProperty("city",         new JObject(new JProperty("de", "Jena"))),
                                       new JProperty("country",      "DEU")
                                   )),
                                   new JProperty("geoLocation",  new JObject(new JProperty("lat", 50.9221), new JProperty("lng", 11.5852))),
                                   new JProperty("created",      created.ToISO8601()),
                                   new JProperty("chargingStations", new JArray(
                                       Station(ACStation, ACEVSE, "AC_ThreePhases", 22000, "IEC_62196_T2",       "SOCKET", ACTariff, "meter-ac-1", "AC-2026-001", acMeterKey, created),
                                       Station(DCStation, DCEVSE, "DC",             150000, "IEC_62196_T2_COMBO", "CABLE",  DCTariff, "meter-dc-1", "DC-2026-001", dcMeterKey, created)
                                   ))
                               )))
                           )))
                       );

            if (!RoamingNetwork.TryParse(json, out var network, out var error))
                throw new ArgumentException("The roaming network of the tests is no roaming network: " + error);

            var history = RoamingNetworkHistory.Start(network);

            history.AttestationSigners      = [ new RoamingNetworkCommitSigner(rnoSigning, rnoSigningKey.Id, COSEAlgorithm.ES256, RoamingNetworkCommitSignatureRoles.RoamingNetworkOperator) ];
            history.PriceStatementSigners   = [ new RoamingNetworkCommitSigner(gefSigning, gefSigningKey.Id, COSEAlgorithm.ES256, RoamingNetworkCommitSignatureRoles.ChargingStationOperator, GEF.ToString()) ];

            if (!history.TryAttest(out _, out error, now))
                throw new ArgumentException("The head of the roaming network of the tests cannot be attested: " + error);

            // An ad hoc price of the DC EVSE, signed by DE*GEF: a little less
            // than its tariff.
            var prices = new JObject(new JProperty("prices", new JArray(new JObject(
                             new JProperty("chargingTariffId",  DCTariff),
                             new JProperty("elements",          new JArray(Element(0.65m)))
                         )))).ToString(Newtonsoft.Json.Formatting.None);

            var result = history.RuntimeStatus.TryUpdate(POIRuntimeStatusKind.PriceStatus, nameof(EVSE), DCEVSE, prices, now.AddSeconds(-1), out _, out error);

            if (result != POIStatusUpdateResult.Updated)
                throw new ArgumentException($"The price of {DCEVSE} was not stated: {result} {error}");

            var httpServer  = new HTTPServer(IPAddress: IPv4Address.Localhost, TCPPort: IPPort.Zero, AutoStart: true);
            var api         = new POI_API(httpServer);

            api.AddRoamingNetworkHistory(history);

            var anchor      = new RoamingNetworkTrustAnchor(NetworkId,
                                                            [ org.GraphDefined.Vanaheimr.Hermod.HTTP.URL.Parse($"http://127.0.0.1:{httpServer.TCPPort}/RNs/{NetworkId}") ],
                                                            [ rnoIdentityKey.Id ]);

            return new TestRoamingNetwork(httpServer, api, history, anchor, acMeterKey, dcMeterKey);

        }

        #endregion


        #region (private static) Station(...) / Tariff(...) / Element(Price)

        private static JObject Station(String          StationId,
                                       String          EVSEId,
                                       String          CurrentType,
                                       Int32           MaxPower,
                                       String          Connector,
                                       String          Format,
                                       String          TariffId,
                                       String          MeterId,
                                       String          Serial,
                                       String          MeterKey,
                                       DateTimeOffset  Created)

            => new (new JProperty("@id",      StationId),
                    new JProperty("created",  Created.ToISO8601()),
                    new JProperty("EVSEs",    new JArray(new JObject(
                        new JProperty("@id",                 EVSEId),
                        new JProperty("created",             Created.ToISO8601()),
                        new JProperty("currentTypes",        new JArray(CurrentType)),
                        new JProperty("maxPower",            MaxPower),
                        new JProperty("chargingConnectors",  new JArray(new JObject(
                            new JProperty("@id",     "1"),
                            new JProperty("type",    Connector),
                            new JProperty("format",  Format)
                        ))),
                        new JProperty("chargingTariffIds",   new JArray(TariffId)),
                        new JProperty("energyMeter",         new JObject(
                            new JProperty("@id",               MeterId),
                            new JProperty("serialNumber",      Serial),
                            new JProperty("signatureMethods",  new JArray("OCMF")),
                            new JProperty("publicKeys",        new JArray(new MeasurementPublicKey(Convert.FromBase64String(MeterKey)).ToJSON())),
                            new JProperty("created",           Created.ToISO8601())
                        ))
                    ))));

        private static JObject Tariff(String          Id,
                                      String          Name,
                                      Decimal         Price,
                                      DateTimeOffset  Created)

            => new (new JProperty("@id",       Id),
                    new JProperty("name",      new JObject(new JProperty("en", Name))),
                    new JProperty("currency",  "EUR"),
                    new JProperty("elements",  new JArray(Element(Price))),
                    new JProperty("created",   Created.ToISO8601()));

        private static JObject Element(Decimal Price)

            => new (new JProperty("priceComponents", new JArray(new JObject(
                        new JProperty("type",      "ENERGY"),
                        new JProperty("price",     Price),
                        new JProperty("stepSize",  1)
                    ))));

        #endregion

        #region (private static) NewKey(Curve) / DER(Key)

        private static ECPrivateKeyParameters NewKey(String Curve)
        {

            var parameters = (Curve == "secp521r1" ? COSECurve.P521 : COSECurve.P256).DomainParameters!;

            return new ECPrivateKeyParameters(new BigInteger(1, RandomNumberGenerator.GetBytes(parameters.N.BitLength / 8 + 8)).Mod(parameters.N.Subtract(BigInteger.One)).Add(BigInteger.One),
                                              parameters);

        }

        /// <summary>
        /// The DER encoded SubjectPublicKeyInfo of the public key of the given
        /// private key, as Base64: what an energy meter names.
        /// </summary>
        private static String DER(ECPrivateKeyParameters Key)

            => Convert.ToBase64String(SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(
                   new ECPublicKeyParameters("EC", Key.Parameters.G.Multiply(Key.D).Normalize(), SecObjectIdentifiers.SecP256r1)
               ).GetDerEncoded());

        #endregion


        public async ValueTask DisposeAsync()
        {
            await API.DisposeAsync();
            await HTTPServer.DisposeAsync();
        }

    }

}
