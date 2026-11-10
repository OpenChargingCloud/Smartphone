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
using System.Globalization;

using Newtonsoft.Json.Linq;

using Org.BouncyCastle.Crypto;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.protocols.WWCP.POI;

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// The receipt of what the web payment page of the operator of a station
    /// took for a session: the amount, with VAT, and its currency, of which
    /// payment and EVSE, and when it was taken - signed, where the page signs
    /// it, by its operator with a signing key the proof of the station attests
    /// and that may sign payments, as WWCP_POI signs a SessionPriceAgreement:
    /// the canonical JSON of what it names but its signature.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An unsigned receipt is what the page says. A signed one is what the
    /// operator of the station signed, and so is evidence of what it took.
    /// </para>
    /// <para>
    /// Its amount is a decimal as text - "14.00" -, as WWCP_POI keeps prices
    /// as they are written: a JSON number is written one way as a decimal and
    /// another as a double, so its canonical JSON, and what is signed, would
    /// not be one.
    /// </para>
    /// </remarks>
    public sealed class WebPaymentReceipt
    {

        #region Data

        /// <summary>
        /// What a receipt names as its "@context".
        /// </summary>
        public const String JSONLDContext = "https://open.charging.cloud/contexts/wwcp/webPayment/receipt/v1";

        private static readonly String[] Signature = [ "role", "signer", "signedAt", "algorithm", "keyId", "value" ];

        #endregion

        #region Properties

        /// <summary>
        /// The receipt as it came.
        /// </summary>
        public JObject          JSON              { get; }

        /// <summary>
        /// The EVSE of the session.
        /// </summary>
        public String           EVSEId            { get; }

        /// <summary>
        /// The reference of the payment, where it names one.
        /// </summary>
        public String?          PaymentReference  { get; }

        /// <summary>
        /// What was taken, with VAT.
        /// </summary>
        public Decimal          Amount            { get; }

        /// <summary>
        /// Its currency.
        /// </summary>
        public String           Currency          { get; }

        /// <summary>
        /// When it was taken.
        /// </summary>
        public DateTimeOffset   CapturedAt        { get; }

        /// <summary>
        /// Whether it is signed.
        /// </summary>
        public Boolean          Signed
            => JSON["value"] is not null;

        /// <summary>
        /// The signing key, where it is signed.
        /// </summary>
        public String?          KeyId
            => JSON.Value<String>("keyId");

        #endregion

        #region (private) Constructor(s)

        private WebPaymentReceipt(JObject         JSON,
                                  String          EVSEId,
                                  String?         PaymentReference,
                                  Decimal         Amount,
                                  String          Currency,
                                  DateTimeOffset  CapturedAt)
        {
            this.JSON              = JSON;
            this.EVSEId            = EVSEId;
            this.PaymentReference  = PaymentReference;
            this.Amount            = Amount;
            this.Currency          = Currency;
            this.CapturedAt        = CapturedAt;
        }

        #endregion


        #region (static) Create(EVSEId, PaymentReference, Amount, Currency, CapturedAt, Signer = null, SigningKey = null, KeyId = null, SignedAt = null)

        /// <summary>
        /// A receipt, as a page gives it - signed by the given operator with the
        /// given key, where one is given.
        /// </summary>
        public static WebPaymentReceipt Create(String                   EVSEId,
                                               String?                  PaymentReference,
                                               Decimal                  Amount,
                                               String                   Currency,
                                               DateTimeOffset           CapturedAt,
                                               String?                  Signer      = null,
                                               AsymmetricKeyParameter?  SigningKey  = null,
                                               String?                  KeyId       = null,
                                               DateTimeOffset?          SignedAt    = null)
        {

            var json = new JObject(new JProperty("@context",    JSONLDContext),
                                   new JProperty("EVSEId",      EVSEId));

            if (PaymentReference is not null)
                json.Add(new JProperty("paymentReference", PaymentReference));

            json.Add(new JProperty("amount",      Amount.ToString(CultureInfo.InvariantCulture)));
            json.Add(new JProperty("currency",    Currency));
            json.Add(new JProperty("capturedAt",  CapturedAt.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)));

            if (Signer is not null && SigningKey is not null && KeyId is not null)
            {

                json.Add(new JProperty("role",       RoamingNetworkCommitSignatureRoles.ChargingStationOperator));
                json.Add(new JProperty("signer",     Signer));
                json.Add(new JProperty("signedAt",   (SignedAt ?? CapturedAt).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)));
                json.Add(new JProperty("algorithm",  COSEAlgorithm.ES256.Name));
                json.Add(new JProperty("keyId",      KeyId));

                json.Add(new JProperty("value",      Convert.ToBase64String(COSEAlgorithm.ES256.Sign(CanonicalJSON.ToUTF8Bytes(json), SigningKey, Deterministic: true))));

            }

            return TryParse(json, out var receipt, out var error)
                       ? receipt
                       : throw new ArgumentException(error);

        }

        #endregion

        #region (static) TryParse(JSON, out Receipt, out Error)

        /// <summary>
        /// A receipt a page gave, or why what it gave is none.
        /// </summary>
        public static Boolean TryParse(JToken?                                      JSON,
                                       [NotNullWhen(true)]  out WebPaymentReceipt?  Receipt,
                                       [NotNullWhen(false)] out String?             Error)
        {

            Receipt  = null;
            Error    = null;

            if (JSON is not JObject json)
            {
                Error = "A receipt is a JSON object.";
                return false;
            }

            if (json["@context"]?.Type != JTokenType.String || json.Value<String>("@context") != JSONLDContext)
            {
                Error = $"It is no receipt of a web payment page: that names the @context '{JSONLDContext}'.";
                return false;
            }

            if (json["EVSEId"]?.Type != JTokenType.String || json["currency"]?.Type != JTokenType.String)
            {
                Error = "A receipt names its 'EVSEId' and its 'currency' as texts.";
                return false;
            }

            if (json["paymentReference"] is JToken reference && reference.Type != JTokenType.String)
            {
                Error = "A receipt names its 'paymentReference' as a text.";
                return false;
            }

            if (json["amount"]?.Type != JTokenType.String ||
                !Decimal.TryParse(json.Value<String>("amount"), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount))
            {
                Error = "A receipt names its 'amount' as a decimal in a text - \"14.00\" -, not less than nothing.";
                return false;
            }

            if (json["capturedAt"]?.Type != JTokenType.String ||
                !DateTimeOffset.TryParse(json.Value<String>("capturedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var capturedAt))
            {
                Error = "A receipt names when it was taken, 'capturedAt', as an ISO 8601 time.";
                return false;
            }

            var signed = Signature.Count(name => json[name] is not null);

            if (signed != 0 && (signed != Signature.Length || Signature.Any(name => json[name]?.Type != JTokenType.String)))
            {
                Error = $"A signed receipt names all of {String.Join(", ", Signature.Select(name => $"'{name}'"))} as texts.";
                return false;
            }

            Receipt = new WebPaymentReceipt(json,
                                            json.Value<String>("EVSEId")!,
                                            json.Value<String>("paymentReference"),
                                            amount,
                                            json.Value<String>("currency")!,
                                            capturedAt);

            return true;

        }

        #endregion

        #region TryVerify(Station, out Error)

        /// <summary>
        /// Whether its signature is the operator's of the given verified
        /// station: of its operator, by a signing key of it the proof attests,
        /// valid when it was signed, that may sign payments.
        /// </summary>
        public Boolean TryVerify(VerifiedChargingStation           Station,
                                 [NotNullWhen(false)] out String?  Error)
        {

            Error = null;

            if (!Signed)
            {
                Error = "It is not signed.";
                return false;
            }

            if (JSON.Value<String>("role")   != RoamingNetworkCommitSignatureRoles.ChargingStationOperator ||
                JSON.Value<String>("signer") != Station.Operator["@id"]?.Value<String>())
            {
                Error = $"It is signed by '{JSON.Value<String>("signer")}', not by the operator of the station, '{Station.Operator["@id"]?.Value<String>()}'.";
                return false;
            }

            if (!DateTimeOffset.TryParse(JSON.Value<String>("signedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var signedAt))
            {
                Error = "It says not when it was signed.";
                return false;
            }

            if (Station.OperatorSigningKeys.FirstOrDefault(key => key.Id == KeyId) is not POIPublicKey key)
            {
                Error = $"It is signed with the key '{KeyId}', which the proof of the station attests not for its operator.";
                return false;
            }

            if (!key.IsValidAt(signedAt) || !key.MaySign(POIKeyUsages.Payments))
            {
                Error = $"It is signed with the key '{KeyId}' of its operator, which may not sign payments then.";
                return false;
            }

            try
            {

                if (!COSEAlgorithm.TryParse(JSON.Value<String>("algorithm")!, out var algorithm) || algorithm.Name != JSON.Value<String>("algorithm"))
                {
                    Error = $"Its signature algorithm '{JSON.Value<String>("algorithm")}' is none this phone knows.";
                    return false;
                }

                var signed = (JObject) JSON.DeepClone();
                signed.Remove("value");

                if (!algorithm.Verify(CanonicalJSON.ToUTF8Bytes(signed), Convert.FromBase64String(JSON.Value<String>("value")!), key.Key.ToPublicKey(), out var signatureError))
                {
                    Error = $"Its signature does not hold: {signatureError}";
                    return false;
                }

            }
            catch (Exception e)
            {
                Error = $"Its signature does not hold: {e.Message}";
                return false;
            }

            return true;

        }

        #endregion

    }

}
