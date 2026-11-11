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

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.chargy.Formats.OCMF;

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// The check of one signed document a live link brought, made when it
    /// came (<see cref="SessionCheck.ValuesOf"/>): whether it holds - true,
    /// false where it is a wrong meter value, null where this phone could not
    /// decide then - and why not, and the meter and its key it was checked
    /// with.
    /// </summary>
    /// <param name="CheckedAt">When it was checked.</param>
    /// <param name="Holds">Whether it holds; null where it could not be checked.</param>
    /// <param name="Error">Why it does not hold, or was not checked.</param>
    /// <param name="MeterSerialNumber">The meter, as the document names it.</param>
    /// <param name="MeterId">The meter, as the proof names it, where its key was found.</param>
    /// <param name="KeyId">The key it was checked with: the meter's, or the operator's that signed it.</param>
    /// <param name="Result">What ChargyCore said of its signature, where it was asked.</param>
    /// <param name="Identification">What the document says identified the charging - its "ID" -, where it was read.</param>
    /// <param name="SignedBy">Whose key its signature is of, where one's is: the meter's, or - of a value between the start and the end - its operator's (<see cref="MeterValueCheck.ByMeter"/>, <see cref="MeterValueCheck.ByOperator"/>).</param>
    /// <param name="OperatorId">The operator, where its key signed it.</param>
    public sealed record LiveLinkValueCheck(DateTimeOffset  CheckedAt,
                                            Boolean?        Holds,
                                            String?         Error,
                                            String?         MeterSerialNumber  = null,
                                            String?         MeterId            = null,
                                            String?         KeyId              = null,
                                            String?         Result             = null,
                                            String?         Identification     = null,
                                            String?         SignedBy           = null,
                                            String?         OperatorId         = null)
    {

        public JObject ToJSON()

            => new (new JProperty("checkedAt",          CheckedAt.ToString("o")),
                    new JProperty("holds",              Holds),
                    new JProperty("error",              Error),
                    new JProperty("meterSerialNumber",  MeterSerialNumber),
                    new JProperty("meterId",            MeterId),
                    new JProperty("keyId",              KeyId),
                    new JProperty("signedBy",           SignedBy),
                    new JProperty("operatorId",         OperatorId),
                    new JProperty("result",             Result),
                    new JProperty("identification",     Identification));

        /// <summary>
        /// A check as the store kept it, or null where it kept none.
        /// </summary>
        public static LiveLinkValueCheck? TryParse(JToken? JSON)

            => JSON is JObject json                                                                                 &&
               json.Value<String>("checkedAt") is String checkedAt                                                    &&
               DateTimeOffset.TryParse(checkedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
                   ? new LiveLinkValueCheck(at,
                                            json["holds"]?.Type == JTokenType.Boolean ? json.Value<Boolean>("holds") : null,
                                            json.Value<String>("error"),
                                            json.Value<String>("meterSerialNumber"),
                                            json.Value<String>("meterId"),
                                            json.Value<String>("keyId"),
                                            json.Value<String>("result"),
                                            json.Value<String>("identification"),
                                            json.Value<String>("signedBy"),
                                            json.Value<String>("operatorId"))
                   : null;

    }


    /// <summary>
    /// The stop this phone asked the web payment page of the operator for,
    /// over the stop link the page gave: when, who asked - the driver, or
    /// this phone itself where a signed value is a wrong one -, whether the
    /// page took it, and what it answered.
    /// </summary>
    /// <param name="RequestedAt">When it was asked for.</param>
    /// <param name="RequestedBy">Who asked.</param>
    /// <param name="Accepted">Whether the page took it: it answered with a 2xx.</param>
    /// <param name="Answer">What the page answered, or why it could not be asked.</param>
    public sealed record LiveLinkStop(DateTimeOffset  RequestedAt,
                                      String          RequestedBy,
                                      Boolean         Accepted,
                                      String          Answer)
    {

        public JObject ToJSON()

            => new (new JProperty("requestedAt",  RequestedAt.ToString("o")),
                    new JProperty("requestedBy",  RequestedBy),
                    new JProperty("accepted",     Accepted),
                    new JProperty("answer",       Answer));

        /// <summary>
        /// A stop as the store kept it, or null where it kept none.
        /// </summary>
        public static LiveLinkStop? TryParse(JToken? JSON)

            => JSON is JObject json                                                                                     &&
               json.Value<String>("requestedAt") is String requestedAt                                                    &&
               DateTimeOffset.TryParse(requestedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)        &&
               json["accepted"]?.Type == JTokenType.Boolean
                   ? new LiveLinkStop(at,
                                      json.Value<String>("requestedBy") ?? "",
                                      json.Value<Boolean>("accepted"),
                                      json.Value<String>("answer")      ?? "")
                   : null;

    }


    /// <summary>
    /// The dispute this phone sent the web payment page of the operator, over
    /// the dispute link the page gave: when, who asked - this phone itself,
    /// where a signed value is a wrong one or none signed the end, or the
    /// driver, asking again -, why, whether the page took it, and what it
    /// answered.
    /// </summary>
    /// <param name="RequestedAt">When it was sent.</param>
    /// <param name="RequestedBy">Who asked.</param>
    /// <param name="Reason">Why the session is disputed, as it was sent.</param>
    /// <param name="Accepted">Whether the page took it: it answered with a 2xx.</param>
    /// <param name="Answer">What the page answered, or why it could not be asked.</param>
    public sealed record LiveLinkDispute(DateTimeOffset  RequestedAt,
                                         String          RequestedBy,
                                         String          Reason,
                                         Boolean         Accepted,
                                         String          Answer)
    {

        public JObject ToJSON()

            => new (new JProperty("requestedAt",  RequestedAt.ToString("o")),
                    new JProperty("requestedBy",  RequestedBy),
                    new JProperty("reason",       Reason),
                    new JProperty("accepted",     Accepted),
                    new JProperty("answer",       Answer));

        /// <summary>
        /// A dispute as the store kept it, or null where it kept none.
        /// </summary>
        public static LiveLinkDispute? TryParse(JToken? JSON)

            => JSON is JObject json                                                                                     &&
               json.Value<String>("requestedAt") is String requestedAt                                                    &&
               DateTimeOffset.TryParse(requestedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)        &&
               json["accepted"]?.Type == JTokenType.Boolean
                   ? new LiveLinkDispute(at,
                                         json.Value<String>("requestedBy") ?? "",
                                         json.Value<String>("reason")      ?? "",
                                         json.Value<Boolean>("accepted"),
                                         json.Value<String>("answer")      ?? "")
                   : null;

    }


    /// <summary>
    /// How the receipt of the web payment page held against the session: of
    /// its EVSE and its payment, signed by the operator of its station where
    /// it is signed, and not more than the cost this phone works out of its
    /// signed values at its tariff - or why not, or why that was not decided.
    /// </summary>
    /// <param name="CheckedAt">When it was checked.</param>
    /// <param name="Holds">Whether it holds; null where it was not decided.</param>
    /// <param name="Error">Why it does not, or why it was not decided.</param>
    /// <param name="Amount">What the page took, with VAT, as the receipt says.</param>
    /// <param name="Currency">Its currency.</param>
    /// <param name="CapturedAt">When it was taken, as the receipt says.</param>
    /// <param name="Cost">What this phone works out the session costs, where it could.</param>
    /// <param name="Signed">Whether the receipt is signed - and its signature the operator's, where it holds.</param>
    /// <param name="KeyId">The key it is signed with.</param>
    /// <param name="PaymentReference">The payment it names.</param>
    public sealed record LiveLinkReceiptCheck(DateTimeOffset   CheckedAt,
                                              Boolean?         Holds,
                                              String?          Error,
                                              Decimal          Amount,
                                              String           Currency,
                                              DateTimeOffset   CapturedAt,
                                              Decimal?         Cost,
                                              Boolean          Signed,
                                              String?          KeyId             = null,
                                              String?          PaymentReference  = null)
    {

        public JObject ToJSON()

            => new (new JProperty("checkedAt",         CheckedAt.ToString("o")),
                    new JProperty("holds",             Holds),
                    new JProperty("error",             Error),
                    new JProperty("amount",            Amount),
                    new JProperty("currency",          Currency),
                    new JProperty("capturedAt",        CapturedAt.ToString("o")),
                    new JProperty("cost",              Cost),
                    new JProperty("signed",            Signed),
                    new JProperty("keyId",             KeyId),
                    new JProperty("paymentReference",  PaymentReference));

        /// <summary>
        /// A check as the store kept it, or null where it kept none.
        /// </summary>
        public static LiveLinkReceiptCheck? TryParse(JToken? JSON)

            => JSON is JObject json                                                                                                   &&
               DateTimeOffset.TryParse(json.Value<String>("checkedAt"),  CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)       &&
               DateTimeOffset.TryParse(json.Value<String>("capturedAt"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var captured) &&
               json["amount"] is JValue { Type: JTokenType.Integer or JTokenType.Float } amount
                   ? new LiveLinkReceiptCheck(at,
                                              json["holds"]?.Type == JTokenType.Boolean ? json.Value<Boolean>("holds") : null,
                                              json.Value<String>("error"),
                                              amount.AsDecimal(),
                                              json.Value<String>("currency") ?? "",
                                              captured,
                                              json["cost"] is JValue { Type: JTokenType.Integer or JTokenType.Float } cost ? cost.AsDecimal() : null,
                                              json.Value<Boolean?>("signed") == true,
                                              json.Value<String>("keyId"),
                                              json.Value<String>("paymentReference"))
                   : null;

    }


    /// <summary>
    /// The payment of a session, as the web payment page says it over the
    /// payment link it gave: when this phone asked last, and why nothing came,
    /// where nothing did. The price agreement of the operator of the station -
    /// the SessionPriceAgreement of WWCP_POI it signed for the session -, as it
    /// came, whether it holds - of this session, its start value and a price
    /// in force at its start, signed by the operator with a key that may sign
    /// prices - and the tariff it agreed, which the session is charged by
    /// where it holds. And the receipt of what the page took, as it came, and
    /// how it held against the cost this phone works out.
    /// </summary>
    /// <param name="AskedAt">When this phone asked the page last.</param>
    /// <param name="Problem">Why nothing came, where nothing did.</param>
    /// <param name="Agreement">The price agreement, as it came.</param>
    /// <param name="AgreementAt">When it came.</param>
    /// <param name="AgreementHolds">Whether it holds; null while it was not checked.</param>
    /// <param name="AgreementError">Why it does not, or why it was not checked yet.</param>
    /// <param name="AgreedTariff">The tariff it agreed, where it holds.</param>
    /// <param name="Receipt">The receipt, as it came.</param>
    /// <param name="ReceiptAt">When it came.</param>
    /// <param name="ReceiptCheck">How it held, once it was checked.</param>
    public sealed record LiveLinkPayment(DateTimeOffset?        AskedAt         = null,
                                         String?                Problem         = null,
                                         JObject?               Agreement       = null,
                                         DateTimeOffset?        AgreementAt     = null,
                                         Boolean?               AgreementHolds  = null,
                                         String?                AgreementError  = null,
                                         SessionTariff?         AgreedTariff    = null,
                                         JObject?               Receipt         = null,
                                         DateTimeOffset?        ReceiptAt       = null,
                                         LiveLinkReceiptCheck?  ReceiptCheck    = null)
    {

        /// <summary>
        /// Why this phone disputes the session for its payment: its price
        /// agreement does not hold, or its receipt does not - the page took
        /// more than the cost, or another payment's. Null where neither is so.
        /// </summary>
        public String? DisputeBecause
            => AgreementHolds == false
                   ? $"The price agreement of its web payment page does not hold: {AgreementError}"
                   : ReceiptCheck?.Holds == false
                         ? $"The receipt of its web payment page does not hold: {ReceiptCheck.Error}"
                         : null;

        /// <summary>
        /// The payment as the pages show it.
        /// </summary>
        public JObject ToJSON()

            => new (new JProperty("askedAt",    AskedAt?.ToString("o")),
                    new JProperty("problem",    Problem),
                    new JProperty("agreement",  Agreement is null
                                                    ? null
                                                    : new JObject(
                                                          new JProperty("cameAt",                AgreementAt?.ToString("o")),
                                                          new JProperty("holds",                 AgreementHolds),
                                                          new JProperty("error",                 AgreementError),
                                                          new JProperty("tariffId",              Agreement.Value<String>("chargingTariffId")),
                                                          new JProperty("priceStatementDigest",  Agreement.Value<String>("priceStatementDigest")),
                                                          new JProperty("signer",                Agreement.Value<String>("signer")),
                                                          new JProperty("signedAt",              Agreement.Value<String>("signedAt")),
                                                          new JProperty("keyId",                 Agreement.Value<String>("keyId"))
                                                      )),
                    new JProperty("receipt",    ReceiptJSON()));

        private JObject? ReceiptJSON()
        {

            if (Receipt is null)
                return null;

            var json = ReceiptCheck?.ToJSON() ?? new JObject(
                                                     new JProperty("holds",  JValue.CreateNull()),
                                                     new JProperty("error",  "It is checked once the session ended and its end value was decided.")
                                                 );

            json.AddFirst(new JProperty("cameAt", ReceiptAt?.ToString("o")));

            return json;

        }

        /// <summary>
        /// What the store keeps of it: all of it, its agreement and its receipt
        /// as they came.
        /// </summary>
        public JObject ToStorageJSON()
        {

            var json = new JObject();

            if (AskedAt        is not null)  json.Add(new JProperty("askedAt",         AskedAt.Value.ToString("o")));
            if (Problem        is not null)  json.Add(new JProperty("problem",         Problem));
            if (Agreement      is not null)  json.Add(new JProperty("agreement",       Agreement.DeepClone()));
            if (AgreementAt    is not null)  json.Add(new JProperty("agreementAt",     AgreementAt.Value.ToString("o")));
            if (AgreementHolds is not null)  json.Add(new JProperty("agreementHolds",  AgreementHolds.Value));
            if (AgreementError is not null)  json.Add(new JProperty("agreementError",  AgreementError));
            if (AgreedTariff   is not null)  json.Add(new JProperty("agreedTariff",    AgreedTariff.ToJSON()));
            if (Receipt        is not null)  json.Add(new JProperty("receipt",         Receipt.DeepClone()));
            if (ReceiptAt      is not null)  json.Add(new JProperty("receiptAt",       ReceiptAt.Value.ToString("o")));
            if (ReceiptCheck   is not null)  json.Add(new JProperty("receiptCheck",    ReceiptCheck.ToJSON()));

            return json;

        }

        /// <summary>
        /// A payment as the store kept it, or null where it kept none.
        /// </summary>
        public static LiveLinkPayment? TryParse(JToken? JSON)
        {

            if (JSON is not JObject json)
                return null;

            static DateTimeOffset? Time(JToken? Token)
                => Token?.Type == JTokenType.String && DateTimeOffset.TryParse(Token.Value<String>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                       ? time
                       : null;

            return new LiveLinkPayment(Time(json["askedAt"]),
                                       json.Value<String>("problem"),
                                       json["agreement"] as JObject,
                                       Time(json["agreementAt"]),
                                       json["agreementHolds"]?.Type == JTokenType.Boolean ? json.Value<Boolean>("agreementHolds") : null,
                                       json.Value<String>("agreementError"),
                                       SessionTariff.TryParse(json["agreedTariff"]),
                                       json["receipt"] as JObject,
                                       Time(json["receiptAt"]),
                                       LiveLinkReceiptCheck.TryParse(json["receiptCheck"]));

        }

    }


    /// <summary>
    /// One signed document a live link brought, as it came: an OCMF document
    /// "OCMF|{payload}|{signature}", when it came, and over which transport
    /// from where - and how it held, once it was checked.
    /// </summary>
    public sealed record LiveLinkDocument(DateTimeOffset       ReceivedAt,
                                          String               Transport,
                                          String               Endpoint,
                                          String               Text,
                                          LiveLinkValueCheck?  Check  = null)
    {

        #region Data

        /// <summary>
        /// The parts of a transaction of an OCMF reading ("TX") that end it:
        /// its end, ended at the station, ended remotely, aborted, the power
        /// lost.
        /// </summary>
        internal static readonly HashSet<String> EndingTransactions = [ "E", "L", "R", "A", "P" ];

        /// <summary>
        /// Its readings, read once of the text they were read of.
        /// </summary>
        private (String Text, IReadOnlyList<MeterReading> Readings)? read;

        #endregion

        #region Properties

        /// <summary>
        /// Its readings, as it says them.
        /// </summary>
        public IReadOnlyList<MeterReading> Readings
        {
            get
            {

                if (read is (String text, IReadOnlyList<MeterReading> readings) && ReferenceEquals(text, Text))
                    return readings;

                var fresh = LiveLinkFollow.ReadingsOf(Text);

                read = (Text, fresh);

                return fresh;

            }
        }

        /// <summary>
        /// Its reading of the begin of the transaction ("B"), where it holds one.
        /// </summary>
        public MeterReading? Begin
            => Readings.FirstOrDefault(reading => reading.Transaction == "B");

        /// <summary>
        /// Whether one of its readings ends the transaction.
        /// </summary>
        public Boolean Ends
            => Readings.Any(reading => reading.Transaction is String transaction && EndingTransactions.Contains(transaction));

        #endregion


        #region ToJSON() / TryParse(JSON)

        /// <summary>
        /// What the store keeps of it: all of it, its text as it came.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(new JProperty("receivedAt",  ReceivedAt.ToString("o")),
                                   new JProperty("transport",   Transport),
                                   new JProperty("endpoint",    Endpoint),
                                   new JProperty("text",        Text));

            if (Check is not null)
                json.Add(new JProperty("check", Check.ToJSON()));

            return json;

        }

        public static LiveLinkDocument? TryParse(JToken? JSON)

            => JSON is JObject json                                                                   &&
               json.Value<String>("receivedAt") is String receivedAt                                    &&
               DateTimeOffset.TryParse(receivedAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) &&
               json.Value<String>("text") is { Length: > 0 } text
                   ? new LiveLinkDocument(at,
                                          json.Value<String>("transport") ?? "",
                                          json.Value<String>("endpoint")  ?? "",
                                          text,
                                          LiveLinkValueCheck.TryParse(json["check"]))
                   : null;

        #endregion

    }


    /// <summary>
    /// The live link of a charging session that the web payment page of the
    /// operator of its station started - not this phone - as the driver handed
    /// it over, and what following it brought so far: how often something came,
    /// when last, over which transport from where, what ChargyCore made of it,
    /// and the signed documents among it, each kept as it came, with how it
    /// held.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keys a live link names are no authority: every signed document it
    /// brings is checked as it comes, with the key the proofs of the station
    /// give for its meter, and against the values that held before it - its
    /// start value first (<see cref="Start"/>), its end value last
    /// (<see cref="End"/>). A wrong one before the end stops the session here
    /// (<see cref="Wrong"/>), and the station, where the page gave a stop
    /// link. A wrong end value (<see cref="WrongEnd"/>) - or none, where the
    /// station ended the stream - stops nothing that still charges: the
    /// session is disputed (<see cref="DisputeBecause"/>), as one stopped for
    /// a wrong value is.
    /// </para>
    /// <para>
    /// The page may give a stop link as well (<see cref="StopLink"/>): a URL
    /// of this session alone, which this phone sends a POST to, to stop it
    /// (<see cref="Stop"/>) - followed on until the meter signs its end. And
    /// the limit the driver chose there (<see cref="Limit"/>), as OCPP 2.1
    /// says it to the station: what its signed values say at the end is
    /// compared with it (<see cref="LiveLinkEnding"/>). And a dispute link
    /// (<see cref="DisputeLink"/>), a URL of this session alone as well,
    /// which this phone sends why it disputes the session to, with the signed
    /// values it disputes (<see cref="Dispute"/>).
    /// </para>
    /// <para>
    /// And the reference of the payment the page took
    /// (<see cref="PaymentReference"/>), which the station writes into the
    /// identification ("ID") of every signed value of the session: a value
    /// that names another, or none, is of another session - a wrong one. Where
    /// the page gave none, that the values are of this very session is not
    /// proven: the live link names the charge point alone.
    /// </para>
    /// <para>
    /// What the page gave is bound to the operator of the station where it
    /// came from the host of the web payment page the proof of the station
    /// names (<see cref="WebPaymentPage"/>): a stop link or a dispute link at
    /// that host (<see cref="StopLinkBound"/>, <see cref="DisputeLinkBound"/>),
    /// and the payment reference of a hand-over the phone took from there
    /// (<see cref="HandOver"/>, <see cref="PaymentReferenceBound"/>). What was
    /// typed or pasted, or is at another host, is the word of whoever gave it.
    /// </para>
    /// <para>
    /// The page may give a payment link as well (<see cref="PaymentLink"/>),
    /// which says the payment of the session (<see cref="Payment"/>): the price
    /// agreement of the operator of the station for it, once its start value
    /// is signed - the session is charged by the tariff it agreed, where it
    /// holds -, and the receipt of what the page took, once it took it, which
    /// is compared with the cost this phone works out. Where either does not
    /// hold, the session is disputed for it.
    /// </para>
    /// </remarks>
    /// <param name="Link">The live link, its JSON document as it was handed over or fetched.</param>
    /// <param name="From">The URL it was fetched from, or null where the document itself was handed over.</param>
    /// <param name="HandedOverAt">When it was handed over.</param>
    /// <param name="Updates">How often something came over it.</param>
    /// <param name="LastUpdateAt">When something came last.</param>
    /// <param name="LastTransport">Over which transport.</param>
    /// <param name="LastEndpoint">From where.</param>
    /// <param name="LastKind">What ChargyCore made of it: the type it read it as.</param>
    /// <param name="Problem">Why nothing comes now, where nothing does.</param>
    /// <param name="EndedAt">When it ended: the meter signed the end of the transaction, or the station ended the stream.</param>
    /// <param name="EndedBecause">Why it is taken as ended.</param>
    /// <param name="Documents">The signed documents it brought, each once, in the order they came.</param>
    /// <param name="StopLink">The stop link the web payment page gave for the session, where it gave one.</param>
    /// <param name="Limit">The limit the driver chose at the page, as OCPP 2.1 says it, where it was handed over.</param>
    /// <param name="Stop">The stop this phone asked the page for last, where it asked for one.</param>
    /// <param name="DisputeLink">The dispute link the web payment page gave for the session, where it gave one.</param>
    /// <param name="Dispute">The dispute this phone sent the page last, where it sent one.</param>
    /// <param name="PaymentReference">The reference of the payment the page took, which every signed value of the session names as its identification, where the page gave one.</param>
    /// <param name="WebPaymentPage">The web payment page the proof of the station named when the live link was handed over, where it named one.</param>
    /// <param name="HandOver">The URL of the hand-over the page gave, at the host of that page, where the phone took it from there - null where what the page gave was typed or pasted.</param>
    /// <param name="PaymentLink">The payment link the web payment page gave for the session, where it gave one.</param>
    /// <param name="Payment">What its payment link said so far.</param>
    public sealed record LiveLinkFollow(JObject                           Link,
                                        String?                           From,
                                        DateTimeOffset                    HandedOverAt,
                                        Int32                             Updates        = 0,
                                        DateTimeOffset?                   LastUpdateAt   = null,
                                        String?                           LastTransport  = null,
                                        String?                           LastEndpoint   = null,
                                        String?                           LastKind       = null,
                                        String?                           Problem        = null,
                                        DateTimeOffset?                   EndedAt        = null,
                                        String?                           EndedBecause   = null,
                                        IReadOnlyList<LiveLinkDocument>?  Documents      = null,
                                        String?                           StopLink       = null,
                                        TransactionLimit?                 Limit          = null,
                                        LiveLinkStop?                     Stop           = null,
                                        String?                           DisputeLink       = null,
                                        LiveLinkDispute?                  Dispute           = null,
                                        String?                           PaymentReference  = null,
                                        String?                           WebPaymentPage    = null,
                                        String?                           HandOver          = null,
                                        String?                           PaymentLink       = null,
                                        LiveLinkPayment?                  Payment           = null)
    {

        #region Data

        /// <summary>
        /// How many signed documents are kept at most: the first, the one that
        /// holds the start, and the latest ones.
        /// </summary>
        public const Int32 MaxDocuments = 1000;

        /// <summary>
        /// The readings of the documents they were read of, whether each held
        /// and whose key signed it, and the first document with the begin of
        /// the transaction among them: read once for every list of documents -
        /// a copy made with other documents reads anew.
        /// </summary>
        private sealed record ReadingsOfDocuments(IReadOnlyList<LiveLinkDocument>?                                         Documents,
                                                  IReadOnlyList<(MeterReading Reading, Boolean? Holds, String? SignedBy)>  Checked,
                                                  IReadOnlyList<MeterReading>                            Readings,
                                                  IReadOnlyList<MeterReading>                            Counted,
                                                  (LiveLinkDocument Document, MeterReading Reading)?     Start);

        private ReadingsOfDocuments? readings;

        #endregion

        #region Properties

        /// <summary>
        /// The signed documents it brought, each once, in the order they came.
        /// </summary>
        public IReadOnlyList<LiveLinkDocument> Signed
            => Documents ?? [];

        /// <summary>
        /// The EVSE the live link names, where it names one: its charging
        /// station's "EVSE", by its "@id".
        /// </summary>
        public String? EVSEId
            => Link["chargingStation"]?["EVSE"]?["@id"] is JValue { Type: JTokenType.String } id
                   ? id.Value<String>()
                   : null;

        /// <summary>
        /// The readings of its signed documents, each once, in the order the
        /// meter measured them - as the documents say.
        /// </summary>
        public IReadOnlyList<MeterReading> Readings
            => Read().Readings;

        /// <summary>
        /// The readings that count, in the order the meter measured them: all
        /// but those of a wrong signed value - those that held, and those not
        /// checked or not decided yet.
        /// </summary>
        public IReadOnlyList<MeterReading> Counted
            => Read().Counted;

        /// <summary>
        /// The signed document of its start, of those it brought: the first
        /// that came with a reading of the begin of the transaction ("B"),
        /// and that reading - or null while none came.
        /// </summary>
        public (LiveLinkDocument Document, MeterReading Reading)? StartDocument
            => Read().Start;

        /// <summary>
        /// How its signed start value held, once it was checked.
        /// </summary>
        public LiveLinkValueCheck? Start
            => StartDocument?.Document.Check;

        /// <summary>
        /// The reading that ends the transaction, of its readings in the order
        /// the meter measured them - or null while none came.
        /// </summary>
        public MeterReading? EndReading
            => Readings.FirstOrDefault(reading => reading.Transaction is String transaction && LiveLinkDocument.EndingTransactions.Contains(transaction));

        /// <summary>
        /// Where its stop link goes, for the pages and the log: its host - not
        /// the link itself, which stops the session for anyone who has it.
        /// </summary>
        public String? StopLinkHost
            => WebPaymentHandOver.HostOf(StopLink);

        /// <summary>
        /// Where the web payment page the proof of the station names is: its
        /// host - what binds a link to the operator of the station.
        /// </summary>
        public String? WebPaymentHost
            => WebPaymentHandOver.HostOf(WebPaymentPage);

        /// <summary>
        /// Where the hand-over the phone took came from: its host - not the
        /// link, which gives the stop link to anyone who has it.
        /// </summary>
        public String? HandOverHost
            => WebPaymentHandOver.HostOf(HandOver);

        /// <summary>
        /// Whether its stop link is bound to the operator of the station: it is
        /// at the host of the web payment page the proof of the station names.
        /// </summary>
        public Boolean StopLinkBound
            => IsBound(StopLinkHost);

        /// <summary>
        /// Whether its dispute link is bound to the operator of the station: it
        /// is at the host of the web payment page the proof of the station
        /// names.
        /// </summary>
        public Boolean DisputeLinkBound
            => IsBound(DisputeLinkHost);

        /// <summary>
        /// Whether its payment reference is bound to the operator of the
        /// station: it came with a hand-over the phone took from the host of
        /// the web payment page the proof of the station names.
        /// </summary>
        public Boolean PaymentReferenceBound
            => PaymentReference is not null && IsBound(HandOverHost);

        /// <summary>
        /// Where its payment link goes, for the pages and the log: its host.
        /// </summary>
        public String? PaymentLinkHost
            => WebPaymentHandOver.HostOf(PaymentLink);

        /// <summary>
        /// Whether its payment link is bound to the operator of the station: it
        /// is at the host of the web payment page the proof of the station
        /// names.
        /// </summary>
        public Boolean PaymentLinkBound
            => IsBound(PaymentLinkHost);

        private Boolean IsBound(String? Host)
            => Host is not null && WebPaymentHost is String webPayment && String.Equals(Host, webPayment, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the page took a stop this phone asked for.
        /// </summary>
        public Boolean StopAccepted
            => Stop?.Accepted == true;

        /// <summary>
        /// The first signed document it brought that is a wrong meter value
        /// before the end - none of its readings ends the transaction: this
        /// phone stopped the session - or null.
        /// </summary>
        public LiveLinkDocument? Wrong
            => Signed.FirstOrDefault(document => document.Check?.Holds == false && !document.Ends);

        /// <summary>
        /// Whether one of its signed values before the end is a wrong one:
        /// this phone stopped the session.
        /// </summary>
        public Boolean Refused
            => Wrong is not null;

        /// <summary>
        /// Why this phone stopped the session, where it did.
        /// </summary>
        public String? RefusedBecause
            => Wrong is LiveLinkDocument wrong
                   ? ReferenceEquals(wrong, StartDocument?.Document)
                         ? $"Its signed start meter value is a wrong one: {wrong.Check!.Error}"
                         : $"A signed meter value after its start is a wrong one: {wrong.Check!.Error}"
                   : null;

        /// <summary>
        /// The first signed document it brought that ends the transaction and
        /// is a wrong one - of the end alone, or the whole transaction from its
        /// begin to its end -, or null.
        /// </summary>
        public LiveLinkDocument? WrongEnd
            => Signed.FirstOrDefault(document => document.Check?.Holds == false && document.Ends);

        /// <summary>
        /// The signed document of its end: the wrong one, where one is, else
        /// the first that came with a reading that ends the transaction - or
        /// null while none came.
        /// </summary>
        public LiveLinkDocument? EndDocument
            => WrongEnd ?? Signed.FirstOrDefault(document => document.Ends);

        /// <summary>
        /// How its signed end value held, once it was checked.
        /// </summary>
        public LiveLinkValueCheck? End
            => EndDocument?.Check;

        /// <summary>
        /// Why this phone disputes the session, where it does: one of its
        /// signed values before the end is a wrong one - it stopped it -, its
        /// signed end value is a wrong one, or the station ended the stream of
        /// its live link and its meter signed no end. Null where its signed
        /// values hold, or are not decided.
        /// </summary>
        public String? DisputeBecause
            => RefusedBecause ??
               (WrongEnd is LiveLinkDocument wrongEnd
                    ? $"Its signed end meter value is a wrong one: {wrongEnd.Check!.Error}"
                    : EndedAt is not null && EndReading is null
                          ? "The station ended the stream of its live link, and its meter signed no end of the transaction: no signed end value says what was charged."
                          : Payment?.DisputeBecause);

        /// <summary>
        /// The signed documents a dispute is sent with, as they came: the one
        /// of its start, and the one it disputes - the wrong one, or the last
        /// that came where none signed the end.
        /// </summary>
        public IReadOnlyList<String> Disputed
            => [.. new[] {
                       StartDocument?.Document,
                       Wrong ?? WrongEnd ?? (EndedAt is not null && EndReading is null ? Signed.LastOrDefault() : Payment?.DisputeBecause is not null ? EndDocument : null)
                   }.OfType<LiveLinkDocument>().
                     Select  (document => document.Text).
                     Distinct()];

        /// <summary>
        /// Where its dispute link goes, for the pages and the log: its host.
        /// </summary>
        public String? DisputeLinkHost
            => WebPaymentHandOver.HostOf(DisputeLink);

        /// <summary>
        /// Whether the session is disputed still, and the page gave a dispute
        /// link this phone sent nothing to yet.
        /// </summary>
        public Boolean DisputePending
            => DisputeBecause is not null && DisputeLink is not null && Dispute is null;

        /// <summary>
        /// The signed documents to check now: those not checked, or not
        /// decided - its end value as well.
        /// </summary>
        public IReadOnlyList<LiveLinkDocument> ToCheck
            => [.. Signed.Where(document => document.Check?.Holds is null &&
                                            document.Readings.Count > 0)];

        /// <summary>
        /// What was charged as its readings that count say: the energy register
        /// of the latest less that of the first - or null while there are not
        /// two readings with an energy register.
        /// </summary>
        public Decimal? EnergyKWh
        {
            get
            {

                var withEnergy = Counted.Where(reading => reading.EnergyKWh is not null).ToArray();

                return withEnergy.Length >= 2
                           ? Math.Max(0m, withEnergy[^1].EnergyKWh!.Value - withEnergy[0].EnergyKWh!.Value)
                           : withEnergy.Length == 1 ? 0m : null;

            }
        }

        private ReadingsOfDocuments Read()
        {

            if (readings is ReadingsOfDocuments read && ReferenceEquals(read.Documents, Documents))
                return read;

            var all    = new Dictionary<(DateTimeOffset, Decimal?, String?), (MeterReading Reading, Boolean? Holds, String? SignedBy)>();
            (LiveLinkDocument, MeterReading)? start = null;

            foreach (var document in Signed)
            {

                var signedBy = document.Check?.Holds == true ? document.Check.SignedBy : null;

                foreach (var reading in document.Readings)
                {

                    var key = (reading.MeasuredAt, reading.EnergyKWh, reading.Transaction);

                    // Of a reading in more than one document: how the first
                    // that was decided held - and the meter's, where one the
                    // meter signed holds it as well.
                    if (!all.TryGetValue(key, out var known) || (known.Holds is null && document.Check?.Holds is not null))
                        all[key] = (known.Reading ?? reading, document.Check?.Holds, signedBy);

                    else if (known.Holds == true && signedBy == MeterValueCheck.ByMeter)
                        all[key] = known with { SignedBy = signedBy };

                }

                if (start is null && document.Begin is MeterReading begin)
                    start = (document, begin);

            }

            IReadOnlyList<(MeterReading Reading, Boolean? Holds, String? SignedBy)> ordered = [.. all.Values.OrderBy(reading => reading.Reading.MeasuredAt)];

            var fresh = new ReadingsOfDocuments(Documents,
                                                ordered,
                                                [.. ordered.Select(reading => reading.Reading)],
                                                [.. ordered.Where (reading => reading.Holds != false).Select(reading => reading.Reading)],
                                                start);

            readings = fresh;

            return fresh;

        }

        #endregion


        #region WithUpdate(At, Transport, Endpoint, Kind, Texts)

        /// <summary>
        /// Something came over it: counted, and its signed documents kept that
        /// were not kept yet - ended, where one of their readings ends the
        /// transaction.
        /// </summary>
        public LiveLinkFollow WithUpdate(DateTimeOffset       At,
                                         String               Transport,
                                         String               Endpoint,
                                         String               Kind,
                                         IEnumerable<String>  Texts)
        {

            var documents  = Signed.ToList();
            var start      = StartDocument?.Document;

            foreach (var text in Texts)
                if (!documents.Any(document => document.Text == text))
                    documents.Add(new LiveLinkDocument(At, Transport, Endpoint, text));

            // The first and the one of the start stay: what goes is the oldest
            // after the first that is not the one of the start.
            while (documents.Count > MaxDocuments)
                documents.RemoveAt(documents.Count > 2 && ReferenceEquals(documents[1], start) ? 2 : 1);

            var updated = this with {
                              Updates        = Updates + 1,
                              LastUpdateAt   = At,
                              LastTransport  = Transport,
                              LastEndpoint   = Endpoint,
                              LastKind       = Kind,
                              Problem        = null,
                              Documents      = documents
                          };

            return EndedAt is null && updated.Readings.FirstOrDefault(reading => reading.Transaction is String transaction && LiveLinkDocument.EndingTransactions.Contains(transaction)) is MeterReading end
                       ? updated with { EndedAt = At, EndedBecause = $"The meter signed the end of the transaction (\"{end.Transaction}\") at {end.MeasuredAt:o}." }
                       : updated;

        }

        #endregion

        #region WithChecks(Checks)

        /// <summary>
        /// How its signed documents held, as checked now - those not decided
        /// before: a document that held or did not stays as it was.
        /// </summary>
        public LiveLinkFollow WithChecks(IEnumerable<(String Text, LiveLinkValueCheck Check)> Checks)
        {

            var checks = Checks.ToDictionary(check => check.Text, check => check.Check);

            return checks.Count == 0
                       ? this
                       : this with {
                             Documents = [.. Signed.Select(document => document.Check?.Holds is null && checks.TryGetValue(document.Text, out var check)
                                                                           ? document with { Check = check }
                                                                           : document)]
                         };

        }

        #endregion

        #region ReadingsOf(Text)

        /// <summary>
        /// The readings of an OCMF document, or none where it is none.
        /// </summary>
        public static IReadOnlyList<MeterReading> ReadingsOf(String Text)
        {

            try
            {

                var scanned = new OCMFDocumentScanner().Scan([ Text ]);

                return scanned.Documents.Count == 1 &&
                       SessionCheck.TryReadingsOf(scanned.Documents[0].Payload["RD"], out var readings, out _)
                           ? readings
                           : [];

            }
            catch (Exception)
            {
                return [];
            }

        }

        #endregion


        #region ToJSON()

        /// <summary>
        /// The live link as the pages show it: where it came from, what came
        /// over it, the readings its signed documents say and whether each
        /// held, how its signed start value held - null while none came -, how
        /// many of the values after it held, the wrong one before the end,
        /// where one is, and how its signed end value held. Where its stop link
        /// goes - its host, not the link -, the limit the driver chose, and the
        /// stop this phone asked for. Why this phone disputes the session,
        /// where its dispute link goes, and the dispute it sent. And the
        /// reference of the payment its signed values are to name. Where the
        /// web payment page the proof of the station names is, where the
        /// hand-over came from, and which of what the page gave is bound to
        /// the operator of the station by that.
        /// </summary>
        public JObject ToJSON()
        {

            var start  = StartDocument;
            var after  = Signed.Where(document => !ReferenceEquals(document, start?.Document)).ToArray();
            var wrong  = Wrong is LiveLinkDocument refused && !ReferenceEquals(refused, start?.Document) ? refused : null;
            var end    = EndDocument;

            JObject CheckJSON(LiveLinkValueCheck Check, String Name, JToken Readings)
            {
                var json = Check.ToJSON();
                json.Add(new JProperty(Name, Readings));
                return json;
            }

            return new (new JProperty("handedOverAt",   HandedOverAt.ToString("o")),
                        new JProperty("from",           From),
                        new JProperty("evseId",         EVSEId),
                        new JProperty("transports",     new JArray((Link["liveTransports"] as JArray ?? []).
                                                                       OfType<JObject>().
                                                                       Select(transport => transport.Value<String>("type")).
                                                                       OfType<String>())),
                        new JProperty("updates",        Updates),
                        new JProperty("lastUpdateAt",   LastUpdateAt?.ToString("o")),
                        new JProperty("lastTransport",  LastTransport),
                        new JProperty("lastEndpoint",   LastEndpoint),
                        new JProperty("lastKind",       LastKind),
                        new JProperty("problem",        Problem),
                        new JProperty("endedAt",        EndedAt?.ToString("o")),
                        new JProperty("endedBecause",   EndedBecause),
                        new JProperty("documents",      Signed.Count),
                        new JProperty("readings",       new JArray(Read().Checked.Select(reading => {
                                                            var json = reading.Reading.ToJSON();
                                                            json.Add(new JProperty("holds",     reading.Holds));
                                                            json.Add(new JProperty("signedBy",  reading.SignedBy));
                                                            return json;
                                                        }))),
                        new JProperty("energyKWh",      EnergyKWh),
                        new JProperty("start",          start?.Document.Check is LiveLinkValueCheck startCheck
                                                            ? CheckJSON(startCheck, "reading", start.Value.Reading.ToJSON())
                                                            : null),
                        new JProperty("values",         new JObject(
                                                            new JProperty("holding",     after.Count(document => document.Check?.Holds == true)),
                                                            new JProperty("byOperator",  after.Count(document => document.Check?.Holds == true && document.Check.SignedBy == MeterValueCheck.ByOperator)),
                                                            new JProperty("wrong",       after.Count(document => document.Check?.Holds == false)),
                                                            new JProperty("undecided",   after.Count(document => document.Check is not null && document.Check.Holds is null)),
                                                            new JProperty("notChecked",  after.Count(document => document.Check is null))
                                                        )),
                        new JProperty("wrong",          wrong?.Check is LiveLinkValueCheck wrongCheck
                                                            ? CheckJSON(wrongCheck, "readings", new JArray(wrong.Readings.Select(reading => reading.ToJSON())))
                                                            : null),
                        new JProperty("refused",        Refused),
                        new JProperty("end",            end?.Check is LiveLinkValueCheck endCheck
                                                            ? CheckJSON(endCheck, "readings", new JArray(end.Readings.Select(reading => reading.ToJSON())))
                                                            : null),
                        new JProperty("stopLink",       StopLinkHost),
                        new JProperty("limit",          Limit?.ToJSON()),
                        new JProperty("stop",           Stop?. ToJSON()),
                        new JProperty("disputeBecause", DisputeBecause),
                        new JProperty("disputeLink",    DisputeLinkHost),
                        new JProperty("dispute",        Dispute?.ToJSON()),
                        new JProperty("paymentReference", PaymentReference),
                        new JProperty("webPaymentHost",   WebPaymentHost),
                        new JProperty("handOver",         HandOverHost),
                        new JProperty("bound",            new JObject(
                                                              new JProperty("stopLink",          StopLinkBound),
                                                              new JProperty("disputeLink",       DisputeLinkBound),
                                                              new JProperty("paymentReference",  PaymentReferenceBound),
                                                              new JProperty("paymentLink",       PaymentLinkBound)
                                                          )),
                        new JProperty("paymentLink",      PaymentLinkHost),
                        new JProperty("payment",          Payment?.ToJSON()));

        }

        #endregion

        #region ToStorageJSON() / TryParse(JSON)

        /// <summary>
        /// What the store keeps of it: all of it, its documents as they came,
        /// each with how it held.
        /// </summary>
        public JObject ToStorageJSON()
        {

            var json = new JObject(
                           new JProperty("link",          Link.DeepClone()),
                           new JProperty("handedOverAt",  HandedOverAt.ToString("o")),
                           new JProperty("updates",       Updates)
                       );

            if (From          is not null)  json.Add(new JProperty("from",           From));
            if (LastUpdateAt  is not null)  json.Add(new JProperty("lastUpdateAt",   LastUpdateAt.Value.ToString("o")));
            if (LastTransport is not null)  json.Add(new JProperty("lastTransport",  LastTransport));
            if (LastEndpoint  is not null)  json.Add(new JProperty("lastEndpoint",   LastEndpoint));
            if (LastKind      is not null)  json.Add(new JProperty("lastKind",       LastKind));
            if (Problem       is not null)  json.Add(new JProperty("problem",        Problem));
            if (EndedAt       is not null)  json.Add(new JProperty("endedAt",        EndedAt.Value.ToString("o")));
            if (EndedBecause  is not null)  json.Add(new JProperty("endedBecause",   EndedBecause));

            json.Add(new JProperty("documents", new JArray(Signed.Select(document => document.ToJSON()))));

            if (StopLink      is not null)  json.Add(new JProperty("stopLink",       StopLink));
            if (Limit         is not null)  json.Add(new JProperty("limit",          Limit.ToJSON()));
            if (Stop          is not null)  json.Add(new JProperty("stop",           Stop. ToJSON()));
            if (DisputeLink   is not null)  json.Add(new JProperty("disputeLink",    DisputeLink));
            if (Dispute       is not null)  json.Add(new JProperty("dispute",        Dispute.ToJSON()));
            if (PaymentReference is not null) json.Add(new JProperty("paymentReference", PaymentReference));
            if (WebPaymentPage   is not null) json.Add(new JProperty("webPaymentPage",   WebPaymentPage));
            if (HandOver         is not null) json.Add(new JProperty("handOver",         HandOver));
            if (PaymentLink      is not null) json.Add(new JProperty("paymentLink",      PaymentLink));
            if (Payment          is not null) json.Add(new JProperty("payment",          Payment.ToStorageJSON()));

            return json;

        }

        /// <summary>
        /// A live link as the store kept it, or null where it kept none - the
        /// check of its start, as a store kept it beside its documents before
        /// each kept its own, with the document of the start.
        /// </summary>
        public static LiveLinkFollow? TryParse(JToken? JSON)
        {

            if (JSON is not JObject json || json["link"] is not JObject link)
                return null;

            static DateTimeOffset? Time(JToken? Token)
                => Token?.Type == JTokenType.String && DateTimeOffset.TryParse(Token.Value<String>(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
                       ? time
                       : null;

            if (Time(json["handedOverAt"]) is not DateTimeOffset handedOverAt)
                throw new FormatException("Its live link says not when it was handed over.");

            IReadOnlyList<LiveLinkDocument> documents = [.. (json["documents"] as JArray ?? []).Select(LiveLinkDocument.TryParse).OfType<LiveLinkDocument>()];

            if (json["start"] is JObject start && start.Value<String>("text") is String startText && LiveLinkValueCheck.TryParse(start) is LiveLinkValueCheck startCheck)
                documents = [.. documents.Select(document => document.Text == startText && document.Check is null ? document with { Check = startCheck } : document)];

            // A limit kept was read when it was handed over: one that can not
            // be read now is lost, and not the session with it.
            var limit = TransactionLimit.TryParse(json["limit"], out var kept, out _) ? kept : null;

            return new LiveLinkFollow((JObject) link.DeepClone(),
                                      json.Value<String>("from"),
                                      handedOverAt,
                                      json["updates"]?.Type == JTokenType.Integer ? json.Value<Int32>("updates") : 0,
                                      Time(json["lastUpdateAt"]),
                                      json.Value<String>("lastTransport"),
                                      json.Value<String>("lastEndpoint"),
                                      json.Value<String>("lastKind"),
                                      json.Value<String>("problem"),
                                      Time(json["endedAt"]),
                                      json.Value<String>("endedBecause"),
                                      documents,
                                      json.Value<String>("stopLink"),
                                      limit,
                                      LiveLinkStop.   TryParse(json["stop"]),
                                      json.Value<String>("disputeLink"),
                                      LiveLinkDispute.TryParse(json["dispute"]),
                                      json.Value<String>("paymentReference"),
                                      json.Value<String>("webPaymentPage"),
                                      json.Value<String>("handOver"),
                                      json.Value<String>("paymentLink"),
                                      LiveLinkPayment.TryParse(json["payment"]));

        }

        #endregion

    }

}
