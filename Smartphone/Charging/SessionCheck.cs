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
using System.Text;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;

using cloud.charging.open.chargy.Formats.OCMF;
using cloud.charging.open.protocols.WWCP.POI;
using cloud.charging.open.Smartphone.Roaming;

using ChargyDictionary  = cloud.charging.open.chargy.I18NDictionary;
using ChargyResult      = cloud.charging.open.chargy.VerificationResult;

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// One reading of a signed meter value: when it was measured, as the meter
    /// signed it, the energy register then, in kWh, which part of the
    /// transaction it is - "B" its begin, "E" its end - its status, "G" where
    /// the meter says it is good, and the errors the meter flags of it ("EF":
    /// "E" of the energy, "t" of the time), where it flags any.
    /// </summary>
    public sealed record MeterReading(DateTimeOffset  MeasuredAt,
                                      Decimal?        EnergyKWh,
                                      String?         Transaction,
                                      String?         Status,
                                      String?         ErrorFlags  = null)
    {

        public JObject ToJSON()
        {

            var json = new JObject(new JProperty("measuredAt",   MeasuredAt.ToString("o")),
                                   new JProperty("energyKWh",    EnergyKWh),
                                   new JProperty("transaction",  Transaction),
                                   new JProperty("status",       Status));

            if (ErrorFlags is { Length: > 0 })
                json.Add(new JProperty("errorFlags", ErrorFlags));

            return json;

        }

    }


    /// <summary>
    /// One signed meter value of a charge detail record, checked: an OCMF
    /// document, signed by the key the proofs kept of the session's station
    /// give for the energy meter of its EVSE at the time it was measured - or,
    /// a value between the start and the end, by a key the proofs give its
    /// operator for its charge transparency records then - or why not.
    /// </summary>
    /// <param name="Nature">What the record calls it: "Start", "End", "Intermediate".</param>
    /// <param name="Verified">Whether its signature is the meter's - or, of a value between the start and the end, its operator's.</param>
    /// <param name="Error">Why not.</param>
    /// <param name="Result">What ChargyCore said of its signature, where it was asked.</param>
    /// <param name="MeterSerialNumber">The meter, as the document names it.</param>
    /// <param name="MeterId">The meter, as the proof names it, where its key was found.</param>
    /// <param name="KeyId">The key it was checked with: the meter's, or the operator's that signed it.</param>
    /// <param name="Identification">What the document says identified the charging (its "ID").</param>
    /// <param name="OfThisStart">Whether that is the token or the ticket of the session's start.</param>
    /// <param name="Readings">Its readings.</param>
    /// <param name="SignedBy">Whose key its signature is of, where one's is: <see cref="ByMeter"/> or <see cref="ByOperator"/>.</param>
    /// <param name="OperatorId">The operator, where its key signed it.</param>
    public sealed record MeterValueCheck(String?                      Nature,
                                         Boolean                      Verified,
                                         String?                      Error,
                                         String?                      Result,
                                         String?                      MeterSerialNumber,
                                         String?                      MeterId,
                                         String?                      KeyId,
                                         String?                      Identification,
                                         Boolean                      OfThisStart,
                                         IReadOnlyList<MeterReading>  Readings,
                                         String?                      SignedBy    = null,
                                         String?                      OperatorId  = null)
    {

        /// <summary>
        /// Signed by the key of the meter.
        /// </summary>
        public const String ByMeter     = "meter";

        /// <summary>
        /// Signed by a key of the operator of the station, for its charge
        /// transparency records.
        /// </summary>
        public const String ByOperator  = "operator";

        public JObject ToJSON()

            => new (new JProperty("nature",             Nature),
                    new JProperty("verified",           Verified),
                    new JProperty("error",              Error),
                    new JProperty("result",             Result),
                    new JProperty("meterSerialNumber",  MeterSerialNumber),
                    new JProperty("meterId",            MeterId),
                    new JProperty("keyId",              KeyId),
                    new JProperty("signedBy",           SignedBy),
                    new JProperty("operatorId",         OperatorId),
                    new JProperty("identification",     Identification),
                    new JProperty("ofThisStart",        OfThisStart),
                    new JProperty("readings",           new JArray(Readings.Select(reading => reading.ToJSON()))));

    }


    /// <summary>
    /// The price of a session, checked: the price statement of the operator
    /// of its station in force when it started, of those kept with it, as
    /// WWCP_POI's SessionPriceEvidence establishes it - or why not.
    /// </summary>
    /// <param name="Verification">What was established, each apart.</param>
    /// <param name="Error">Why nothing was.</param>
    /// <param name="PricePerKWh">The price per kWh of the statement selected.</param>
    public sealed record SessionPriceCheck(SessionPriceVerification?  Verification,
                                           String?                    Error,
                                           Decimal?                   PricePerKWh);


    /// <summary>
    /// What a charging session at a station of a roaming network is checked
    /// against, at one time: the signed meter values of its charge detail
    /// record, each with the key of the energy meter the proofs kept of its
    /// station give for when it was measured, and its price, against the
    /// price statements that held when it started - both under the identity
    /// keys trusted here now, not those trusted when it started.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A key a charge detail record names itself is no authority: the key is
    /// the one the roaming network operator attested for the meter of the
    /// EVSE. The times are the ones the meter signed; that the meter's clock
    /// was right, and that the version of the proof was the latest one, is
    /// not proven (WWCP_POI's HistoricalMeterVerification).
    /// </para>
    /// <para>
    /// A value between the start and the end may be signed by its operator in
    /// place of its meter, as many stations do: with a signing key of the
    /// operator the proof attests and that may sign its charge transparency
    /// records then (WWCP_POI's AuthorizedChargingDocumentKeys). The start and
    /// the end are the meter's alone, and so is what was charged: a value the
    /// operator signed shows how the session goes, and counts for nothing.
    /// </para>
    /// <para>
    /// The price is the one the operator of the station stated for its EVSE,
    /// its station or its pool, signed and in force at the start. That the
    /// operator assigned it to this very session is not proven where it signed
    /// no assignment - "unbound" - and it is compared with the price the
    /// phone counted with, and with what the record says it costs.
    /// </para>
    /// </remarks>
    public sealed class SessionCheck
    {

        #region Data

        private static readonly Lazy<ChargyDictionary> chargyDictionary = new (() => ChargyDictionary.Default());

        /// <summary>
        /// The DER encoding of a SubjectPublicKeyInfo of an ECDSA P-256 key up
        /// to its point: what an energy meter's key is given to ChargyCore as.
        /// </summary>
        private const String P256SPKIPrefix = "3059301306072a8648ce3d020106082a8648ce3d030107034200";

        #endregion

        #region Properties

        /// <summary>
        /// When it was checked.
        /// </summary>
        public DateTimeOffset                  CheckedAt          { get; }

        /// <summary>
        /// Why nothing was checked: the identity keys trusted here do not
        /// give the permission to check what was true at the start.
        /// </summary>
        public String?                         Error              { get; }

        /// <summary>
        /// The price, where the session was started through an e-mobility
        /// provider, whose countersigned start is its start document.
        /// </summary>
        public SessionPriceCheck?              Price              { get; }

        /// <summary>
        /// The price per kWh the phone counted with.
        /// </summary>
        public Decimal                         SessionPricePerKWh { get; }

        /// <summary>
        /// The signed meter values of the charge detail record, once one came
        /// with signed data.
        /// </summary>
        public IReadOnlyList<MeterValueCheck>? MeterValues        { get; }

        /// <summary>
        /// Why the signed data of the record were not checked at all.
        /// </summary>
        public String?                         MeterValuesError   { get; }

        /// <summary>
        /// What the charge detail record says was charged, in kWh.
        /// </summary>
        public Decimal?                        CDREnergyKWh       { get; }

        /// <summary>
        /// What the charge detail record says it costs, with VAT.
        /// </summary>
        public Decimal?                        CDRCost            { get; }

        /// <summary>
        /// Whether every signed meter value of the record is the meter's - or,
        /// between the start and the end, its operator's.
        /// </summary>
        public Boolean                         MeterValuesVerified
            => MeterValuesError is null && MeterValues is { Count: > 0 } values && values.All(value => value.Verified);

        /// <summary>
        /// The reading the transaction began with, of those the meter signed.
        /// </summary>
        public MeterReading?                   StartReading
            => Verified.FirstOrDefault(reading => reading.Transaction == "B") ?? Verified.FirstOrDefault();

        /// <summary>
        /// The reading the transaction ended with, of those the meter signed.
        /// </summary>
        public MeterReading?                   EndReading
            => Verified.LastOrDefault(reading => reading.Transaction == "E") ?? Verified.LastOrDefault();

        /// <summary>
        /// What the meter signed was charged, in kWh: its end less its begin.
        /// </summary>
        public Decimal?                        SignedEnergyKWh
            => StartReading is MeterReading start && EndReading is MeterReading end && end != start &&
               start.EnergyKWh is Decimal from && end.EnergyKWh is Decimal to
                   ? to - from
                   : null;

        /// <summary>
        /// What the signed values cost at the price checked: what its tariff
        /// makes of what the meter signed when - every part it prices.
        /// </summary>
        public TariffCost?                     ExpectedTariffCost
        {
            get
            {

                if (Price?.Verification is not SessionPriceVerification verification || verification.Selected is not SelectedSessionPrice selected ||
                    StartReading is not MeterReading start || start.EnergyKWh is not Decimal from || EndReading is not MeterReading end || end == start)
                    return null;

                var points = Verified.Where (reading => reading.EnergyKWh is not null && reading.MeasuredAt >= start.MeasuredAt && reading.MeasuredAt <= end.MeasuredAt).
                                      Select(reading => (reading.MeasuredAt, reading.EnergyKWh!.Value - from)).
                                      ToArray();

                return TariffCost.Of(TariffOf(selected, verification.Station), new ChargingProfile(points));

            }
        }

        /// <summary>
        /// What the signed values cost at the price checked.
        /// </summary>
        public Decimal?                        ExpectedCost
            => ExpectedTariffCost?.Amount;

        /// <summary>
        /// The readings of the values the meter signed: no value its operator
        /// signed says what was charged.
        /// </summary>
        private IEnumerable<MeterReading> Verified
            => (MeterValues ?? []).Where(value => value.Verified && value.SignedBy == MeterValueCheck.ByMeter).
                                   SelectMany(value => value.Readings).
                                   OrderBy(reading => reading.MeasuredAt);

        #endregion

        #region Constructor(s)

        private SessionCheck(DateTimeOffset                   CheckedAt,
                             String?                          Error,
                             SessionPriceCheck?               Price,
                             Decimal                          SessionPricePerKWh,
                             IReadOnlyList<MeterValueCheck>?  MeterValues,
                             String?                          MeterValuesError,
                             Decimal?                         CDREnergyKWh,
                             Decimal?                         CDRCost)
        {
            this.CheckedAt           = CheckedAt;
            this.Error               = Error;
            this.Price               = Price;
            this.SessionPricePerKWh  = SessionPricePerKWh;
            this.MeterValues         = MeterValues;
            this.MeterValuesError    = MeterValuesError;
            this.CDREnergyKWh        = CDREnergyKWh;
            this.CDRCost             = CDRCost;
        }

        #endregion


        #region (static) Of(Configuration, Session, Now)

        /// <summary>
        /// Check the given session now, against the trust anchor and the
        /// identity keys trusted in the given configuration - or null, where it
        /// keeps no evidence: it was started at no station of a roaming network.
        /// </summary>
        public static SessionCheck? Of(RoamingNetworkConfiguration  Configuration,
                                       ChargingSession              Session,
                                       DateTimeOffset               Now)
        {

            if (Session.Evidence is not SessionEvidence evidence)
                return null;

            var cdr        = Session.Report?.CDR;
            var cdrEnergy  = Number(cdr?["total_energy"]);
            var cdrCost    = GrossCostOf(cdr?["total_cost"]);

            if (AuthorityOf(Configuration, evidence, Now, out var authorityError) is not IRoamingNetworkTrustAuthority authority)
                return new SessionCheck(Now, authorityError, null, Session.PricePerKWh, null, null, cdrEnergy, cdrCost);

            // Started through an e-mobility provider, a session is bound to its
            // price by an agreement of when its start request says the provider
            // countersigned it - to the second -, which the operator it went to
            // knows as well; without one, its price is the one in force when the
            // phone started it.
            var price              = Session.Start is not null
                                         ? PriceOf(authority,
                                                   evidence,
                                                   Session.Report?.PriceAgreement is not null ? Session.Start.CountersignedAt : Session.StartedAt,
                                                   Session.Start.RequestDigest,
                                                   Session.Report?.PriceAgreement,
                                                   SessionPriceAgreement.Contract)
                                         : LiveLinkStartOf(Session) is (DateTimeOffset started, String digest)
                                               ? PriceOf(authority, evidence, started, digest, Session.LiveLink!.Payment?.Agreement, SessionPriceAgreement.AdHoc)
                                               : null;

            var (values, problem)  = MeterValuesOf(authority, evidence, Session);

            return new SessionCheck(Now, null, price, Session.PricePerKWh, values, problem, cdrEnergy, cdrCost);

        }

        #endregion

        #region (static) ValuesOf(Configuration, Session, Now)

        /// <summary>
        /// The signed documents of a session the web payment page of the
        /// operator of its station started, as its live link brought them,
        /// checked now - those not checked or not decided yet, in the order
        /// they came: its start value, the reading the meter signed as the
        /// begin of the transaction ("B"), every value after it, and its end
        /// value - a reading that ends the transaction ("E", "L", "R", "A",
        /// "P"), alone or in the whole transaction from its begin to its end
        /// (<see cref="LiveLinkFollow.ToCheck"/>).
        /// </summary>
        /// <remarks>
        /// <para>
        /// A value holds where it is signed by the key the proofs kept give
        /// for the meter the proof of the station names for the EVSE, at the
        /// time it was measured, as ChargyCore checks the signature - a value
        /// between the start value and the end value, of that meter, by a key
        /// they give its operator for its charge transparency records then as
        /// well, which says the begin of the transaction only as the start
        /// value that held says it -; where the
        /// meter says each of its readings is good - its status "G", no error
        /// flagged - and of an energy register; and where it fits the values
        /// that held before it, and its readings each other: not measured
        /// before the start value, and the meter not counting backwards - no
        /// reading measured later with less energy, none measured earlier with
        /// more, none at the same time with another. A begin of the transaction
        /// it says again is the start value that held; and nothing of the
        /// transaction is measured after its end.
        /// </para>
        /// <para>
        /// It does not hold - a wrong meter value - where it is of another
        /// meter, not signed by that key, not good as the meter says, or does
        /// not fit. It is not checked where this phone can not decide: the
        /// identity keys trusted here do not suffice, or no proof kept attests
        /// a key of the meter for the time it was measured - one that names a
        /// time to come as well.
        /// </para>
        /// <para>
        /// Where the page gave the reference of the payment it took, a value
        /// holds only where the meter signed it as its identification ("ID"):
        /// one that names another, or none, is of another session. Where the
        /// page gave none, that the values are of this very session is not
        /// proven: its live link names the charge point alone.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<(LiveLinkDocument Document, LiveLinkValueCheck Check)> ValuesOf(RoamingNetworkConfiguration  Configuration,
                                                                                                    ChargingSession              Session,
                                                                                                    DateTimeOffset               Now)
        {

            if (Session.LiveLink is not LiveLinkFollow follow || follow.ToCheck is not { Count: > 0 } toCheck)
                return [];

            if (Session.Evidence is not SessionEvidence evidence)
                return [.. toCheck.Select(document => (document, new LiveLinkValueCheck(Now, null, "The session keeps no proof of its station to check it with.")))];

            if (AuthorityOf(Configuration, evidence, Now, out var authorityError) is not IRoamingNetworkTrustAuthority authority)
                return [.. toCheck.Select(document => (document, new LiveLinkValueCheck(Now, null, authorityError)))];

            var start   = follow.StartDocument;
            var held    = follow.Signed.Where(document => document.Check?.Holds == true).SelectMany(document => document.Readings).ToList();
            var begin   = start?.Document.Check?.Holds == true ? start.Value.Reading : null;
            var checks  = new List<(LiveLinkDocument, LiveLinkValueCheck)>();

            foreach (var document in toCheck)
            {

                var isStart  = ReferenceEquals(document, start?.Document);
                var check    = CheckValue(authority, evidence, document, isStart, isStart ? null : begin, held, follow.PaymentReference, Session, Now);

                checks.Add((document, check));

                // A wrong one stops the session: nothing after it is checked.
                if (check.Holds == false)
                    break;

                if (check.Holds == true)
                {

                    held.AddRange(document.Readings);

                    if (isStart)
                        begin = start!.Value.Reading;

                }

            }

            return checks;

        }

        #endregion

        #region (private static) CheckValue(Authority, Evidence, Document, IsStart, Begin, Held, Reference, Session, Now)

        /// <summary>
        /// One signed document of a live link, checked: its signature with the
        /// key of the meter, the payment it names, what the meter says of its
        /// readings, and how they fit the begin of the transaction, the
        /// readings that held before and each other - and its end, where it
        /// ends the transaction.
        /// </summary>
        private static LiveLinkValueCheck CheckValue(IRoamingNetworkTrustAuthority  Authority,
                                                     SessionEvidence                Evidence,
                                                     LiveLinkDocument               Document,
                                                     Boolean                        IsStart,
                                                     MeterReading?                  Begin,
                                                     IReadOnlyList<MeterReading>    Held,
                                                     String?                        Reference,
                                                     ChargingSession                Session,
                                                     DateTimeOffset                 Now)
        {

            var value = CheckDocument(Authority, Evidence, Evidence.MeterEvidence,
                                      IsStart ? "Start" : Document.Ends ? "End" : "Intermediate",
                                      Document.Text, Session, out var noKey);

            LiveLinkValueCheck Checked(Boolean? Holds, String? Error)
                => new (Now, Holds, Error, value.MeterSerialNumber, value.MeterId, value.KeyId, value.Result, value.Identification, value.SignedBy, value.OperatorId);

            if (!value.Verified)
                return value.MeterSerialNumber is String named && Evidence.MeterSerialNumber is String expected && named != expected
                           ? Checked(false, $"It is of the meter {named}, and the proof of {Evidence.StationId} names the meter {expected} for {Evidence.EVSEId}.")
                           : Checked(noKey ? null : false, value.Error);

            // Of this very session: the meter signed the payment the web
            // payment page took for it as what identified the charging.
            if (Reference is not null && value.Identification != Reference)
                return Checked(false, value.Identification is null
                                          ? $"It names no identification of the charging (\"ID\"), and the web payment page took the payment \"{Reference}\" for this session: it is not proven to be of it."
                                          : $"It is of another session: it names \"{value.Identification}\" as the identification of the charging (\"ID\"), and the web payment page took the payment \"{Reference}\" for this session.");

            // The meter's: what the meter says of it itself.
            if (NotGood(Document.Readings, IsStart) is String notGood)
                return Checked(false, notGood);

            // Its operator signs no begin: one it says again is compared with
            // the start value the meter signed, once that held.
            if (value.SignedBy == MeterValueCheck.ByOperator && Document.Begin is not null && Begin is null)
                return Checked(null, $"Its operator {value.OperatorId} signed the begin of the transaction again, and no start value the meter signed held yet to compare it with.");

            // How it fits the begin of the transaction, what held before it,
            // its readings each other, and its end.
            return DoesNotFit(Document.Readings, Begin, Held) is String doesNotFit
                       ? Checked(false, doesNotFit)
                       : Checked(true,  null);

        }

        #endregion

        #region (private static) NotGood(Readings, IsStart) / DoesNotFit(Readings, Begin, Held)

        /// <summary>
        /// Whether the given reading ends its transaction.
        /// </summary>
        private static Boolean Ends(MeterReading Reading)
            => Reading.Transaction is String transaction && LiveLinkDocument.EndingTransactions.Contains(transaction);

        /// <summary>
        /// Why the readings of a signed value are not good, as the meter says
        /// it of them itself - a status other than "G", an error flagged, or
        /// no energy register - or null.
        /// </summary>
        /// <param name="Readings">The readings of the value.</param>
        /// <param name="IsStart">Whether the value is the start value of its session.</param>
        private static String? NotGood(IReadOnlyList<MeterReading>  Readings,
                                       Boolean                      IsStart)
        {

            foreach (var reading in Readings)
            {

                var which = IsStart && reading.Transaction == "B"
                                ? "its start value"
                                : Ends(reading)
                                      ? "its end value"
                                      : $"its value of {reading.MeasuredAt:o}";

                if (reading.Status is String status && status != "G")
                    return $"The meter says {which} is not good: its status is \"{status}\"{StatusInWords(status)}.";

                if (reading.ErrorFlags is { Length: > 0 } flags)
                    return $"The meter flags an error of {which}: \"{flags}\"{ErrorFlagsInWords(flags)}.";

                if (reading.EnergyKWh is null)
                    return $"{Capital(which)} is no energy register in kWh or Wh.";

            }

            return null;

        }

        /// <summary>
        /// Why the readings of a signed value do not fit - the begin of the
        /// transaction that held, the readings that held before, each other,
        /// or the end of the transaction - or null.
        /// </summary>
        /// <param name="Readings">The readings of the value, each of an energy register.</param>
        /// <param name="Begin">The begin of the transaction, as the start value that held says it.</param>
        /// <param name="Held">The readings of the values that held before it.</param>
        private static String? DoesNotFit(IReadOnlyList<MeterReading>  Readings,
                                          MeterReading?                Begin,
                                          IReadOnlyList<MeterReading>  Held)
        {

            // A transaction begins once: a begin it says again - the whole
            // transaction, at its end - is the start value that held.
            if (Begin is MeterReading began && Readings.FirstOrDefault(reading => reading.Transaction == "B") is MeterReading again &&
                (again.MeasuredAt != began.MeasuredAt || again.EnergyKWh != began.EnergyKWh))
                return $"It says the transaction began with {Figure(again.EnergyKWh!.Value)} kWh at {again.MeasuredAt:o}, " +
                       $"and the start value that held says {Figure(began.EnergyKWh!.Value)} kWh at {began.MeasuredAt:o}.";

            // How it fits what held before it, and its readings each other.
            foreach (var reading in Readings)
            {

                if (Begin is MeterReading begin && reading.MeasuredAt < begin.MeasuredAt)
                    return $"It was measured at {reading.MeasuredAt:o}, before the start value of {begin.MeasuredAt:o}.";

                foreach (var before in Held.Concat(Readings.Where(other => !ReferenceEquals(other, reading))))
                {

                    if (before.EnergyKWh is not Decimal was)
                        continue;

                    if ((before.MeasuredAt < reading.MeasuredAt && was > reading.EnergyKWh) ||
                        (before.MeasuredAt > reading.MeasuredAt && was < reading.EnergyKWh))
                        return $"The meter counts backwards: {Figure(reading.EnergyKWh!.Value)} kWh at {reading.MeasuredAt:o}, and {Figure(was)} kWh at {before.MeasuredAt:o}.";

                    if (before.MeasuredAt == reading.MeasuredAt && was != reading.EnergyKWh)
                        return $"It says {Figure(reading.EnergyKWh!.Value)} kWh at {reading.MeasuredAt:o}, where a value before says {Figure(was)} kWh at that time.";

                }

            }

            // Nothing of the transaction is measured after its end.
            var all = Held.Concat(Readings).ToArray();

            foreach (var reading in Readings)
            {

                if (Ends(reading) && all.Where(other => other.MeasuredAt > reading.MeasuredAt).MaxBy(other => other.MeasuredAt) is MeterReading later)
                    return $"It ends the transaction at {reading.MeasuredAt:o}, and a value of it was measured after its end, at {later.MeasuredAt:o}.";

                if (all.Where(Ends).Where(other => other.MeasuredAt < reading.MeasuredAt).MinBy(other => other.MeasuredAt) is MeterReading ended)
                    return $"It was measured at {reading.MeasuredAt:o}, after the end of the transaction at {ended.MeasuredAt:o}.";

            }

            return null;

        }

        private static String Capital(String Text)
            => Text.Length > 0 ? Char.ToUpperInvariant(Text[0]) + Text[1..] : Text;

        private static String Figure(Decimal Value)
            => Value.ToString(CultureInfo.InvariantCulture);

        #endregion

        #region (private static) AuthorityOf(Configuration, Evidence, Now, out Error)

        /// <summary>
        /// What the evidence of a session is checked under now: the proof of
        /// its start, under the trust anchor and the identity keys trusted in
        /// the given configuration - or why nothing of it is checked.
        /// </summary>
        private static IRoamingNetworkTrustAuthority? AuthorityOf(RoamingNetworkConfiguration  Configuration,
                                                                  SessionEvidence              Evidence,
                                                                  DateTimeOffset               Now,
                                                                  out String?                  Error)
        {

            Error = null;

            if (!RoamingNetworkProof.TryParse(Evidence.Proofs[0], out var proof, out var proofError))
            {
                Error = $"The proof kept of {Evidence.StationId} is none: {proofError}";
                return null;
            }

            // Of the decisions of now: a key no longer trusted checks nothing,
            // whatever it was trusted for at the start.
            if (Configuration.IsSet && !Configuration.QuorumTrusted)
            {
                Error = $"Nothing of it is checked: the identity keys trusted here are of fewer holders than the trust anchor of '{Configuration.TrustAnchor!.Id}' requires. Configuration → Map trusts them.";
                return null;
            }

            if (Configuration.HistoricalContext(proof, Now, out var authorityError) is not HistoricalTrustContext authority)
            {
                Error = $"Nothing of it is checked: {authorityError}";
                return null;
            }

            return authority;

        }

        #endregion

        #region (private static) PriceOf(Authority, Evidence, Started, StartDocumentSHA256, Agreement, ChargingMode)

        /// <summary>
        /// The price of the session as the price statements kept with it say,
        /// in force when it started - its start document the start as the
        /// provider countersigned it, or the signed start value of its live
        /// link - and bound to it by the price agreement of its operator, where
        /// one is given: one of the charging mode of the session, "contract"
        /// where it was started through an e-mobility provider, "adhoc" where
        /// the web payment page of the operator started it.
        /// </summary>
        private static SessionPriceCheck PriceOf(IRoamingNetworkTrustAuthority  Authority,
                                                 SessionEvidence                Evidence,
                                                 DateTimeOffset                 Started,
                                                 String                         StartDocumentSHA256,
                                                 JToken?                        Agreement,
                                                 String                         ChargingMode)
        {

            if (!Evidence.PriceEvidenceWith(Agreement).TryVerify(Authority,
                                                                 Evidence.EVSEId,
                                                                 Evidence.MeterSerialNumber ?? "",
                                                                 Started,
                                                                 StartDocumentSHA256,
                                                                 out var verification,
                                                                 out var error))
                return new SessionPriceCheck(null, error, null);

            // Of another charging mode, an agreement is of another session than
            // this one, whatever else it names, as WWCP_POI holds one that does
            // not match the session: one of a session paid ad hoc binds nothing
            // of one started through an e-mobility provider, nor the reverse.
            if (verification.Agreement == "valid" &&
                Agreement?["chargingMode"]?.Value<String>() is String mode && mode != ChargingMode)
            {
                verification = verification with {
                                   Applicability  = "invalid",
                                   Agreement      = "invalid",
                                   Selected       = null,
                                   Diagnostics    = [ $"Its price agreement is of a session {ChargingModeInWords(mode)}, and it was {ChargingModeInWords(ChargingMode)}.", .. verification.Diagnostics ]
                               };
            }

            return new SessionPriceCheck(verification,
                                         null,
                                         verification.Selected is SelectedSessionPrice selected
                                             ? StationCheck.EnergyPrice(new JArray(selected.Elements))
                                             : null);

        }

        /// <summary>
        /// How a session of the given charging mode is paid, in words.
        /// </summary>
        private static String ChargingModeInWords(String ChargingMode)

            => ChargingMode switch {
                   SessionPriceAgreement.AdHoc     => "paid ad hoc",
                   SessionPriceAgreement.Contract  => "started through an e-mobility provider",
                   _                               => $"of the charging mode \"{ChargingMode}\""
               };

        #endregion

        #region LiveLinkStartOf(Session)

        /// <summary>
        /// The start of a session of a live link, as its price agreement names
        /// it: when its signed start value says it began, and the SHA-256 of
        /// that document as it came, in UTF-8, as lowercase hex - where its
        /// start value came and held.
        /// </summary>
        public static (DateTimeOffset Started, String StartDocumentSHA256)? LiveLinkStartOf(ChargingSession Session)

            => Session.LiveLink?.StartDocument is (LiveLinkDocument document, MeterReading reading) && document.Check?.Holds == true
                   ? (reading.MeasuredAt, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(document.Text))))
                   : null;

        #endregion

        #region AgreementOf(Configuration, Session, Now)

        /// <summary>
        /// Whether the price agreement the web payment page gave for a session
        /// of its live link holds - of this session and its signed start value,
        /// signed by the operator of its station with a key that may sign
        /// prices, of a price in force at its start (WWCP_POI's
        /// SessionPriceEvidence) - and the tariff it agreed. Null where that is
        /// not decided yet: its start value did not come or hold, or nothing of
        /// it can be checked now.
        /// </summary>
        public static (Boolean? Holds, String? Error, SessionTariff? Tariff) AgreementOf(RoamingNetworkConfiguration  Configuration,
                                                                                         ChargingSession              Session,
                                                                                         DateTimeOffset               Now)
        {

            if (Session.LiveLink?.Payment?.Agreement is not JObject agreement || Session.Evidence is not SessionEvidence evidence)
                return (null, "No price agreement came.", null);

            if (LiveLinkStartOf(Session) is not (DateTimeOffset started, String digest))
                return (null, "It is checked once the signed start value came and held.", null);

            // Signed after now, as this phone's clock says: checked again
            // later, not taken as one of another session for a clock ahead.
            if (agreement["signedAt"]?.Type == JTokenType.String &&
                DateTimeOffset.TryParse(agreement.Value<String>("signedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var signedAt) &&
                signedAt > Now)
                return (null, $"It says it was signed at {signedAt:o}, after now: it is checked again then.", null);

            if (AuthorityOf(Configuration, evidence, Now, out var authorityError) is not IRoamingNetworkTrustAuthority authority)
                return (null, authorityError, null);

            var price = PriceOf(authority, evidence, started, digest, agreement, SessionPriceAgreement.AdHoc);

            if (price.Verification is not SessionPriceVerification verification)
                return (null, price.Error, null);

            if (verification.Agreement == "valid" && verification.Selected is SelectedSessionPrice selected)
                return (true, null, TariffOf(selected, verification.Station, Session.Tariff?.TimeZone));

            return (false,
                    verification.Diagnostics.FirstOrDefault() ?? "It does not select one price of those in force at the start.",
                    null);

        }

        #endregion

        #region ReceiptOf(Configuration, Session, Now)

        /// <summary>
        /// How the receipt the web payment page gave for a session of its live
        /// link holds: a receipt, of its EVSE and its payment, signed by the
        /// operator of its station where it is signed, in the currency of its
        /// tariff - and not more than what this phone works out the session
        /// costs, of its signed values that count, at the tariff it is charged
        /// by. Not decided, where the cost is not exact, or where the signature
        /// can not be checked now.
        /// </summary>
        public static LiveLinkReceiptCheck ReceiptOf(RoamingNetworkConfiguration  Configuration,
                                                     ChargingSession              Session,
                                                     DateTimeOffset               Now)
        {

            if (!WebPaymentReceipt.TryParse(Session.LiveLink?.Payment?.Receipt, out var receipt, out var receiptError))
                return new LiveLinkReceiptCheck(Now, false, $"It is no receipt: {receiptError}", 0m, "", Now, null, false);

            LiveLinkReceiptCheck Check(Boolean? Holds, String? Error, Decimal? Cost = null)
                => new (Now, Holds, Error, receipt.Amount, receipt.Currency, receipt.CapturedAt, Cost, receipt.Signed, receipt.KeyId, receipt.PaymentReference);

            if (receipt.EVSEId != Session.EVSEId)
                return Check(false, $"It is of {receipt.EVSEId}, not of {Session.EVSEId}.");

            if (receipt.PaymentReference is String reference && Session.LiveLink!.PaymentReference is String paid && reference != paid)
                return Check(false, $"It is of the payment \"{reference}\", and the page took the payment \"{paid}\" for this session.");

            if (receipt.Signed)
            {

                if (Session.Evidence is not SessionEvidence evidence ||
                    AuthorityOf(Configuration, evidence, Now, out var authorityError) is not IRoamingNetworkTrustAuthority authority)
                    return Check(null, "Its signature can not be checked now: nothing of the session is.");

                if (LiveLinkStartOf(Session) is not (DateTimeOffset started, String digest) ||
                    PriceOf(authority, evidence, started, digest, null, SessionPriceAgreement.AdHoc).Verification is not SessionPriceVerification verification)
                    return Check(null, "Its signature can not be checked: no signed start value of the session held, which the station is checked of.");

                if (!receipt.TryVerify(verification.Station, out var signatureError))
                    return Check(false, signatureError);

            }

            var cost = Session.CostAt(Now);

            if (cost is null)
                return Check(null, "The session is charged by no tariff this phone could work its cost out of.");

            if (receipt.Currency != cost.Currency)
                return Check(false, $"It took {receipt.Currency}, and the tariff of the session is in {cost.Currency}.", cost.Amount);

            if (!cost.Exact)
                return Check(null, $"The cost of the session could not be worked out exactly: {String.Join(" ", cost.Notes)}", cost.Amount);

            if (receipt.Amount > cost.Amount)
                return Check(false,
                             $"It says the page took {Figure(receipt.Amount)} {receipt.Currency}, and the session costs {Figure(cost.Amount)} {cost.Currency}, as this phone works it out of its signed values at its tariff: " +
                             $"{Figure(receipt.Amount - cost.Amount)} {cost.Currency} more.",
                             cost.Amount);

            return Check(true, null, cost.Amount);

        }

        #endregion

        #region (private static) TariffOf(Selected, Station, TimeZone = null)

        /// <summary>
        /// The tariff of a price selected for a session: the elements its
        /// statement sets, the least and the most its tariff says, and the time
        /// zone of the pool of its station, where it names one.
        /// </summary>
        private static SessionTariff TariffOf(SelectedSessionPrice     Selected,
                                              VerifiedChargingStation  Station,
                                              String?                  TimeZone = null)

            => new (Selected.Currency,
                    new JArray(Selected.Elements.Select(element => element.DeepClone())),
                    Selected.Tariff["@id"]?.Value<String>(),
                    Selected.Tariff["minPrice"] as JObject,
                    Selected.Tariff["maxPrice"] as JObject,
                    TimeZone ?? (Station.ChargingPool["timeZone"]?.Type == JTokenType.String ? Station.ChargingPool["timeZone"]!.Value<String>() : null));

        #endregion

        #region (private static) MeterValuesOf(Authority, Evidence, Session)

        /// <summary>
        /// The signed meter values of the session's charge detail record, each
        /// checked - none, where no record with signed data came yet.
        /// </summary>
        private static (IReadOnlyList<MeterValueCheck>? Values, String? Error) MeterValuesOf(IRoamingNetworkTrustAuthority  Authority,
                                                                                            SessionEvidence                Evidence,
                                                                                            ChargingSession                Session)
        {

            if (Session.Report?.CDR is not JObject cdr)
                return (null, null);

            if (cdr["signed_data"] is not JObject signedData)
                return (null, "The charge detail record carries no signed meter values.");

            var method = signedData.Value<String>("encoding_method");

            if (!String.Equals(method, "OCMF", StringComparison.OrdinalIgnoreCase))
                return ([], $"Its signed meter values are encoded as '{method}', and this phone reads OCMF alone.");

            var values = (signedData["signed_values"] as JArray ?? []).OfType<JObject>().ToArray();

            if (values.Length == 0)
                return ([], "Its signed data holds no signed value.");

            var meterEvidence = Evidence.MeterEvidence;

            return (Fitted([ .. values.Select(value => Check(Authority, Evidence, meterEvidence, value, Session)) ]), null);

        }

        #endregion

        #region (private static) Fitted(Values)

        /// <summary>
        /// The signed values of a charge detail record whose signatures held,
        /// checked as the values of a live link are: what the meter says of
        /// each reading itself - its status "G", no error flagged, an energy
        /// register - and how they fit each other - the meter's start first,
        /// a begin of the transaction said once, the meter not counting
        /// backwards, nothing measured after the end. One that does not is
        /// not verified, and says why; the others are judged without it.
        /// </summary>
        /// <remarks>
        /// The start value - the first the meter signed with the begin of the
        /// transaction ("B"), or of the nature "Start" - is judged first,
        /// wherever the record puts it, then the others in the order of the
        /// record: what a value is compared with are those that held before.
        /// </remarks>
        private static IReadOnlyList<MeterValueCheck> Fitted(MeterValueCheck[] Values)
        {

            var start   = Values.FirstOrDefault(value => value.Verified && value.SignedBy == MeterValueCheck.ByMeter &&
                                                         value.Readings.Any(reading => reading.Transaction == "B")) ??
                          Values.FirstOrDefault(value => value.Verified && value.SignedBy == MeterValueCheck.ByMeter &&
                                                         String.Equals(value.Nature, "Start", StringComparison.OrdinalIgnoreCase));

            var held    = new List<MeterReading>();
            var begin   = (MeterReading?) null;
            var fitted  = Values.ToArray();

            foreach (var index in Enumerable.Range(0, Values.Length).OrderBy(index => ReferenceEquals(Values[index], start) ? 0 : 1))
            {

                var value = Values[index];

                if (!value.Verified)
                    continue;

                var isStart  = ReferenceEquals(value, start);

                var error    = NotGood(value.Readings, isStart) ??
                               (value.SignedBy == MeterValueCheck.ByOperator && begin is null && value.Readings.Any(reading => reading.Transaction == "B")
                                    ? $"Its operator {value.OperatorId} signed the begin of the transaction, and no start value the meter signed held to compare it with."
                                    : null) ??
                               DoesNotFit(value.Readings, isStart ? null : begin, held);

                if (error is not null)
                {
                    fitted[index] = value with { Verified = false, Error = error };
                    continue;
                }

                held.AddRange(value.Readings);

                if (isStart)
                    begin = value.Readings.FirstOrDefault(reading => reading.Transaction == "B");

            }

            return fitted;

        }

        #endregion

        #region (private static) Check(Authority, Evidence, MeterEvidence, Value, Session)

        /// <summary>
        /// One signed meter value of a charge detail record: its OCMF document,
        /// as its signed data or its plain data say it, checked.
        /// </summary>
        private static MeterValueCheck Check(IRoamingNetworkTrustAuthority  Authority,
                                             SessionEvidence                Evidence,
                                             HistoricalMeterEvidence        MeterEvidence,
                                             JObject                        Value,
                                             ChargingSession                Session)
        {

            var nature = Value.Value<String>("nature");

            return OCMFText(Value) is String text
                       ? CheckDocument(Authority, Evidence, MeterEvidence, nature, text, Session, out _)
                       : new MeterValueCheck(nature, false, "It is no OCMF document with a signature, neither as its signed data nor as its plain data.", null, null, null, null, null, false, []);

        }

        #endregion

        #region (private static) CheckDocument(Authority, Evidence, MeterEvidence, Nature, Text, Session, out NoKey)

        /// <summary>
        /// One signed meter value: its OCMF document read by ChargyCore, the key
        /// of its meter at the times of its readings taken of the proofs kept,
        /// and its signature checked by ChargyCore with that key - and, where
        /// that key did not sign it, with the keys the proofs give the operator
        /// of the station for its charge transparency records then. Where it is
        /// neither's, whether that is because no key could be had to check it
        /// with (NoKey) - no proof kept attests one of its meter for its times,
        /// or more than one, nor one of its operator - rather than that it is
        /// none. A value the operator signed holds only between the start and
        /// the end: its nature "Intermediate", and none of its readings ending
        /// the transaction.
        /// </summary>
        private static MeterValueCheck CheckDocument(IRoamingNetworkTrustAuthority  Authority,
                                                     SessionEvidence                Evidence,
                                                     HistoricalMeterEvidence        MeterEvidence,
                                                     String?                        Nature,
                                                     String                         Text,
                                                     ChargingSession                Session,
                                                     out Boolean                    NoKey)
        {

            NoKey = false;

            OCMFDocument document;

            try
            {

                var scanned = new OCMFDocumentScanner().Scan([ Text ]);

                if (scanned.Documents.Count != 1)
                    return new MeterValueCheck(Nature, false, scanned.ErrorMessage ?? $"It holds {scanned.Documents.Count} OCMF documents, not one.", null, null, null, null, null, false, []);

                document = scanned.Documents[0];

            }
            catch (Exception e)
            {
                return new MeterValueCheck(Nature, false, $"Its OCMF document could not be read: {e.Message}", null, null, null, null, null, false, []);
            }

            var payload         = document.Payload;
            var serial          = payload["MS"]?.Type == JTokenType.String ? payload["MS"]!.Value<String>() : null;
            var identification  = payload["ID"]?.Type == JTokenType.String ? payload["ID"]!.Value<String>() : null;
            var ofThisStart     = identification is not null &&
                                  (identification == Session.Start?.Token || identification == Session.Ticket?.Id);

            MeterValueCheck Refused(String Error, IReadOnlyList<MeterReading> Readings)
                => new (Nature, false, Error, null, serial, null, null, identification, ofThisStart, Readings);

            if (!TryReadingsOf(payload["RD"], out var readings, out var readingsError))
                return Refused(readingsError, []);

            if (readings.Count == 0)
                return Refused("It holds no reading.", []);

            if (serial is null)
                return Refused("It names no meter: its \"MS\" is missing.", readings);

            var evseId  = Evidence.EVSEId;
            var times   = readings.Select(reading => reading.MeasuredAt).ToArray();

            // The key of its meter: one, of P-256, at all its times.
            VerifiedMeterKeyBinding? binding  = null;
            String?                  noKey    = null;

            if (!MeterEvidence.TryVerify(Authority, evseId, serial, times, MeasurementKeyFormat.SPKIDER, out var verification, out var bindingError))
                noKey = $"No key of the meter {serial} of {evseId} is attested for when it was measured: {bindingError}";

            else if (verification.Bindings.DistinctBy(attested => attested.Key.CanonicalId).ToArray() is { Length: not 1 } keys)
                noKey = $"Its readings are of times its meter had {keys.Length} different keys.";

            else if (verification.Bindings[0].Key.Key.X is null || verification.Bindings[0].Key.Key.Y is null)
                noKey = $"The key {verification.Bindings[0].Key.Id} of the meter {serial} is no point of P-256.";

            else
                binding = verification.Bindings[0];

            ChargyResult? result = null;

            if (binding is not null)
            {

                try
                {
                    result = Validate(document, binding.Key.Key.X!, binding.Key.Key.Y!);
                }
                catch (Exception e)
                {
                    return Refused($"ChargyCore could not check its signature: {e.Message}", readings);
                }

                if (result == ChargyResult.ValidSignature)
                    return new MeterValueCheck(Nature, true, null, result.ToString(), serial, binding.MeterId, binding.Key.Id, identification, ofThisStart, readings,
                                               MeterValueCheck.ByMeter);

            }

            // Not the meter's: its operator's, where a key the proofs give it
            // for its charge transparency records signed it - of the meter the
            // proof names for the EVSE.
            var operatorMay  = String.Equals(Nature, "Intermediate", StringComparison.OrdinalIgnoreCase) &&
                               !readings.Any(reading => reading.Transaction is String transaction && LiveLinkDocument.EndingTransactions.Contains(transaction));

            var operatorKeys = serial == Evidence.MeterSerialNumber
                                   ? OperatorKeysOf(Authority, Evidence, times)
                                   : [];

            foreach (var key in operatorKeys)
            {

                ChargyResult byOperator;

                try
                {
                    byOperator = Validate(document, key.X, key.Y);
                }
                catch (Exception)
                {
                    continue;
                }

                if (byOperator != ChargyResult.ValidSignature)
                    continue;

                return operatorMay
                           ? new MeterValueCheck(Nature, true, null, byOperator.ToString(), serial, binding?.MeterId, key.Id, identification, ofThisStart, readings,
                                                 MeterValueCheck.ByOperator, key.OperatorId)
                           : new MeterValueCheck(Nature, false,
                                                 $"It is signed by the key {key.Id} of its operator {key.OperatorId}, not by its meter {serial}: only the meter signs the start and the end of a transaction.",
                                                 byOperator.ToString(), serial, binding?.MeterId, key.Id, identification, ofThisStart, readings,
                                                 MeterValueCheck.ByOperator, key.OperatorId);

            }

            // Whether its meter signed it can not be told.
            if (binding is null)
            {
                NoKey = true;
                return Refused(noKey!, readings);
            }

            return new MeterValueCheck(Nature,
                                       false,
                                       $"It is not signed by the key {binding.Key.Id} of the meter {serial}: {result}" +
                                           (operatorMay ? ", nor by a key its operator may sign its charge transparency records with." : "."),
                                       result.ToString(),
                                       serial,
                                       binding.MeterId,
                                       binding.Key.Id,
                                       identification,
                                       ofThisStart,
                                       readings);

        }

        /// <summary>
        /// What ChargyCore says of the signature of an OCMF document, checked
        /// with the given point of a key of P-256.
        /// </summary>
        private static ChargyResult Validate(OCMFDocument  Document,
                                             Byte[]        X,
                                             Byte[]        Y)

            => new OCMFSignatureValidator(chargyDictionary.Value).
                   Validate(Document, P256SPKIPrefix + "04" + Convert.ToHexStringLower(X) + Convert.ToHexStringLower(Y), "hex");

        #endregion

        #region (private static) OperatorKeysOf(Authority, Evidence, Times)

        /// <summary>
        /// A key the operator of a station may sign its charge transparency
        /// records with: its operator, its @id, and its point of P-256.
        /// </summary>
        private sealed record OperatorKey(String  OperatorId,
                                          String  Id,
                                          Byte[]  X,
                                          Byte[]  Y);

        /// <summary>
        /// The keys the operator of the station of the session's EVSE may sign
        /// its charge transparency records with at every one of the given
        /// times, as WWCP_POI gives them of the proofs kept
        /// (AuthorizedChargingDocumentKeys): signing keys of the operator a
        /// proof attests for that time, valid then, and naming the usage
        /// "chargeTransparencyRecords" or none - of P-256, the curve the meter
        /// values are checked with here. None, where no proof kept gives one
        /// for a time.
        /// </summary>
        private static IReadOnlyList<OperatorKey> OperatorKeysOf(IRoamingNetworkTrustAuthority  Authority,
                                                                 SessionEvidence                Evidence,
                                                                 IEnumerable<DateTimeOffset>    Times)
        {

            IReadOnlyList<OperatorKey>? atAll = null;

            foreach (var time in Times.Distinct())
            {

                var atTime = new List<OperatorKey>();

                foreach (var json in Evidence.Proofs)
                {

                    if (!RoamingNetworkProof.TryParse(json, out var proof, out _))
                        continue;

                    foreach (var operatorId in proof.Given(RoamingNetwork.PropertyKeys.ChargingStationOperators).
                                                     Select(chargingStationOperator => chargingStationOperator["@id"]).
                                                     OfType<JValue>().
                                                     Where (id => id.Type == JTokenType.String).
                                                     Select(id => id.Value<String>()!))
                    {

                        if (!AuthorizedChargingDocumentKeys.TryVerify(Authority, proof, Evidence.EVSEId, operatorId, time, out var authorized, out _))
                            continue;

                        foreach (var key in authorized.Keys)
                            if (key.Algorithm == "ECDSA-secp256r1"                                                 &&
                                !atTime.Any(known => known.Id == key.Id)                                           &&
                                COSEKey.TryParse(Convert.FromBase64String(key.Value), out var cose, out _)         &&
                                cose.X is Byte[] x && cose.Y is Byte[] y)
                            {
                                atTime.Add(new OperatorKey(operatorId, key.Id, x, y));
                            }

                    }

                }

                atAll = atAll is null
                            ? atTime
                            : [.. atAll.Where(key => atTime.Any(other => other.Id == key.Id))];

                if (atAll.Count == 0)
                    return [];

            }

            return atAll ?? [];

        }

        #endregion

        #region (private static) OCMFText(Value) / TryReadingsOf(JSON, out Readings, out Error) / OCMFTime(Text)

        /// <summary>
        /// The OCMF document of a signed value - "OCMF|{payload}|{signature}" -
        /// as its signed data or its plain data say it, as text or as Base64.
        /// </summary>
        private static String? OCMFText(JObject Value)
        {

            static Boolean IsOCMF(String Text)
                => Text.StartsWith("OCMF|", StringComparison.Ordinal) && Text.Count(character => character == '|') >= 2;

            foreach (var name in new[] { "signed_data", "plain_data" })
            {

                if (Value[name]?.Type != JTokenType.String || Value.Value<String>(name)!.Trim() is not { Length: > 0 } text)
                    continue;

                if (IsOCMF(text))
                    return text;

                try
                {
                    var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(text)).Trim();

                    if (IsOCMF(decoded))
                        return decoded;
                }
                catch (FormatException)
                {
                    // Neither OCMF nor Base64 of it.
                }

            }

            return null;

        }

        /// <summary>
        /// The readings of an OCMF payload: each with the time the meter signed
        /// and its energy register in kWh - "Wh" taken as a thousandth.
        /// </summary>
        internal static Boolean TryReadingsOf(JToken?                                             JSON,
                                              out IReadOnlyList<MeterReading>                      Readings,
                                              [System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out String?  Error)
        {

            var readings = new List<MeterReading>();

            Readings  = readings;
            Error     = null;

            foreach (var reading in (JSON as JArray ?? []).OfType<JObject>())
            {

                if (OCMFTime(reading["TM"]?.Type == JTokenType.String ? reading["TM"]!.Value<String>() : null) is not DateTimeOffset measuredAt)
                {
                    Error = $"A reading names no time it was measured at as OCMF writes one: '{reading["TM"]}'.";
                    return false;
                }

                var value  = Number(reading["RV"]);
                var unit   = reading["RU"]?.Value<String>();

                readings.Add(new MeterReading(measuredAt,
                                              unit switch {
                                                  "kWh"  => value,
                                                  "Wh"   => value / 1000m,
                                                  _      => null
                                              },
                                              reading["TX"]?.Value<String>(),
                                              reading["ST"]?.Value<String>(),
                                              reading["EF"]?.Type == JTokenType.String ? reading["EF"]!.Value<String>() : null));

            }

            return true;

        }

        /// <summary>
        /// What the status of an OCMF reading ("ST") means, as " (...)" - or
        /// nothing, where OCMF knows no such status.
        /// </summary>
        private static String StatusInWords(String Status)

            => Status switch {
                   "N"  => " (not present)",
                   "T"  => " (timeout)",
                   "D"  => " (disconnected)",
                   "R"  => " (not found)",
                   "M"  => " (manipulated)",
                   "X"  => " (exchanged)",
                   "I"  => " (incompatible)",
                   "O"  => " (out of range)",
                   "S"  => " (substitute)",
                   "E"  => " (system error)",
                   "F"  => " (read error)",
                   _    => ""
               };

        /// <summary>
        /// What the error flags of an OCMF reading ("EF") say, as " (...)":
        /// "E" of its energy, "t" of its time.
        /// </summary>
        private static String ErrorFlagsInWords(String Flags)
        {

            var of = new List<String>();

            if (Flags.Contains('E'))  of.Add("of its energy");
            if (Flags.Contains('t'))  of.Add("of its time");

            return of.Count > 0 ? $" ({String.Join(" and ", of)})" : "";

        }

        /// <summary>
        /// When a reading of an OCMF document was taken, as it writes it:
        /// "2026-10-20T10:00:00,000+0000 S" - its offset without a colon, which
        /// no format string of .NET reads, and how its clock was synchronized.
        /// </summary>
        private static DateTimeOffset? OCMFTime(String? Text)
        {

            if (Text is null || Text.Length < 28)
                return null;

            if (!DateTime.TryParseExact(Text[..23], "yyyy-MM-dd'T'HH:mm:ss','fff", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local) ||
                Text[23] is not ('+' or '-')                                                                                                     ||
                !Int32.TryParse(Text[24..26], NumberStyles.None, CultureInfo.InvariantCulture, out var hours)                                    ||
                !Int32.TryParse(Text[26..28], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes))
                return null;

            return new DateTimeOffset(local, (Text[23] == '-' ? -1 : 1) * new TimeSpan(hours, minutes, 0));

        }

        #endregion

        #region (private static) Number(JSON) / GrossCostOf(JSON)

        private static Decimal? Number(JToken? JSON)

            => JSON is JValue { Type: JTokenType.Integer or JTokenType.Float } number
                   ? number.AsDecimal()
                   : null;

        /// <summary>
        /// What an OCPI price says with VAT - or, as OCPI 2.1.1 writes a price,
        /// a number - and null where it says no more than without.
        /// </summary>
        private static Decimal? GrossCostOf(JToken? JSON)

            => JSON is JObject price
                   ? Number(price["incl_vat"])
                   : Number(JSON);

        #endregion


        #region ToJSON()

        /// <summary>
        /// The check as the pages show it.
        /// </summary>
        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("checkedAt",  CheckedAt.ToString("o")),
                           new JProperty("error",      Error)
                       );

            if (Price is SessionPriceCheck price)
            {

                var verification  = price.Verification;
                var selected      = verification?.Selected;

                json.Add(new JProperty("price", new JObject(
                             new JProperty("error",                   price.Error),
                             new JProperty("signatures",              verification?.Signatures),
                             new JProperty("applicability",           verification?.Applicability),
                             new JProperty("agreement",               verification?.Agreement),
                             new JProperty("publication",             verification?.Publication),
                             new JProperty("publicationBeforeStart",  verification?.PublicationBeforeStart),
                             new JProperty("chainGaps",               verification?.ChainGaps),
                             new JProperty("customerConsent",         verification?.CustomerConsent),
                             new JProperty("diagnostics",             new JArray(verification?.Diagnostics.ToArray() ?? [])),
                             new JProperty("selected",                selected is null ? null : new JObject(
                                 new JProperty("entityType",  selected.Statement.EntityType),
                                 new JProperty("entityId",    selected.Statement.EntityId),
                                 new JProperty("signer",      selected.Statement.Signer.ToString()),
                                 new JProperty("sequence",    selected.Statement.Sequence),
                                 new JProperty("signedAt",    selected.Statement.SignedAt.ToString("o")),
                                 new JProperty("digest",      selected.Digest),
                                 new JProperty("tariffId",    selected.Tariff["@id"]?.Value<String>()),
                                 new JProperty("currency",    selected.Currency),
                                 new JProperty("pricePerKWh", price.PricePerKWh)
                             )),
                             new JProperty("matchesSession",          price.PricePerKWh is Decimal perKWh ? perKWh == SessionPricePerKWh : (Boolean?) null)
                         )));

            }

            if (MeterValues is not null || MeterValuesError is not null)
                json.Add(new JProperty("meterValues", new JObject(
                             new JProperty("verified",      MeterValuesVerified),
                             new JProperty("error",         MeterValuesError),
                             new JProperty("values",        new JArray((MeterValues ?? []).Select(value => value.ToJSON()))),
                             new JProperty("startKWh",      StartReading?.EnergyKWh),
                             new JProperty("endKWh",        EndReading?.  EnergyKWh),
                             new JProperty("energyKWh",     SignedEnergyKWh),
                             new JProperty("cdrEnergyKWh",  CDREnergyKWh),
                             new JProperty("energyMatches", SignedEnergyKWh is Decimal signed && CDREnergyKWh is Decimal reported
                                                                ? Math.Round(signed, 3) == Math.Round(reported, 3)
                                                                : (Boolean?) null),
                             new JProperty("expectedCost",  ExpectedCost),
                             new JProperty("expectedCostExact", ExpectedTariffCost?.Exact),
                             new JProperty("cdrCost",       CDRCost),
                             new JProperty("costMatches",   ExpectedCost is Decimal expected && CDRCost is Decimal cost
                                                                ? expected == Math.Round(cost, 2, MidpointRounding.AwayFromZero)
                                                                : (Boolean?) null),
                             new JProperty("timeBasis",     "signed-measurement-timestamps")
                         )));

            return json;

        }

        #endregion

    }

}
