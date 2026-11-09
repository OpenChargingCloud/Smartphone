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

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// One part of the limit of a session against what its signed values say:
    /// its energy in kWh, its cost in the currency of the tariff, its time in
    /// seconds, or the state of charge in percent, which no meter measures -
    /// and whether it was reached: null where its signed values do not say.
    /// </summary>
    /// <param name="Kind">"energy", "cost", "time" or "soc".</param>
    /// <param name="Limit">The limit, in its unit.</param>
    /// <param name="Unit">"kWh", the currency of the tariff, "s" or "%".</param>
    /// <param name="Value">What the signed values say, in its unit, or null where they say nothing of it.</param>
    /// <param name="Reached">Whether it was reached; null where its signed values do not say.</param>
    public sealed record LimitValue(String    Kind,
                                    Decimal   Limit,
                                    String    Unit,
                                    Decimal?  Value,
                                    Boolean?  Reached)
    {

        public JObject ToJSON()

            => new (new JProperty("kind",     Kind),
                    new JProperty("limit",    Limit),
                    new JProperty("unit",     Unit),
                    new JProperty("value",    Value),
                    new JProperty("reached",  Reached));

        /// <summary>
        /// The limit in words: "20 kWh", "15.00 EUR", "1 h 30 min", "80 %
        /// state of charge".
        /// </summary>
        public String LimitInWords
            => InWords(Limit);

        /// <summary>
        /// What the signed values say in words - or that they say nothing.
        /// </summary>
        public String ValueInWords
            => Value is Decimal value ? InWords(value) : "not measured";

        private String InWords(Decimal Figure)

            => Kind switch {
                   "energy"  => $"{TransactionLimit.KWh(Figure)} kWh",
                   "cost"    => TransactionLimit.Money(Figure, Unit),
                   "time"    => TransactionLimit.Duration(Figure),
                   _         => $"{Figure:0} % state of charge"
               };

    }


    /// <summary>
    /// How a session followed over its live link ended, once it did, against
    /// the limit it was started with: this phone stopped it ("stopped"), it
    /// ended at its limit ("limit"), for another reason ("other") - or its
    /// signed values do not say whether it reached its limit ("unknown"):
    /// only a state of charge, which no meter measures, would have ended it.
    /// Or its signed end value is a wrong one ("wrong"), and says nothing.
    /// </summary>
    /// <remarks>
    /// The station keeps to the limit, and counts from its own start of the
    /// transaction and with its own rounding: a limit counts as reached where
    /// what the signed values say comes within a hundredth of it
    /// (<see cref="Tolerance"/>). What they say rests on the end value as
    /// well: a wrong one is compared with nothing, and a wrong value counts
    /// for nothing.
    /// </remarks>
    /// <param name="Reason">"stopped", "limit", "other", "unknown" or "wrong".</param>
    /// <param name="Because">Why, in a sentence or two.</param>
    /// <param name="Transaction">How the meter says the transaction ended ("TX"), or null where it signed no end.</param>
    /// <param name="Limits">Each part of its limit against what its signed values say.</param>
    public sealed record LiveLinkEnding(String                      Reason,
                                        String                      Because,
                                        String?                     Transaction,
                                        IReadOnlyList<LimitValue>   Limits)
    {

        #region Data

        /// <summary>
        /// How close to its limit what the signed values say comes, at least,
        /// where the limit counts as reached: within a hundredth of it.
        /// </summary>
        public const Decimal Tolerance = 0.01m;

        #endregion


        #region ToJSON()

        public JObject ToJSON()

            => new (new JProperty("reason",       Reason),
                    new JProperty("because",      Because),
                    new JProperty("transaction",  Transaction));

        #endregion

        #region (static) LimitsOf(Session)

        /// <summary>
        /// Each part of the limit of a session followed over its live link
        /// against what its signed values say: from its start value - or its
        /// first reading - to its end value - or its latest reading -, its cost
        /// what the price of the proof of its station makes of that energy.
        /// None where it was started with no limit. A wrong value counts for
        /// nothing: of a wrong end value, up to the latest reading that counts.
        /// </summary>
        public static IReadOnlyList<LimitValue> LimitsOf(ChargingSession Session)
        {

            if (Session.LiveLink is not LiveLinkFollow follow || follow.Limit is not TransactionLimit limit)
                return [];

            var counted = follow.Counted;
            var first   = (follow.Start?.Holds != false ? follow.StartDocument?.Reading : null) ?? counted.FirstOrDefault();
            var last    = (follow.WrongEnd is null      ? follow.EndReading            : null) ?? counted.LastOrDefault();

            Decimal? energy  = first?.EnergyKWh is Decimal from && last?.EnergyKWh is Decimal to
                                   ? Math.Max(0m, to - from)
                                   : null;

            Decimal? seconds = first is not null && last is not null
                                   ? Math.Max(0m, (Decimal) Math.Round((last.MeasuredAt - first.MeasuredAt).TotalSeconds))
                                   : null;

            static Boolean? Reached(Decimal? Value, Decimal Limit)
                => Value is Decimal value ? value >= Limit * (1m - Tolerance) : null;

            var limits = new List<LimitValue>();

            if (limit.MaxEnergy is Decimal maxEnergy)
                limits.Add(new LimitValue("energy", maxEnergy / 1000m, "kWh", energy, Reached(energy, maxEnergy / 1000m)));

            if (limit.MaxCost is Decimal maxCost)
            {
                Decimal? cost = energy is Decimal charged ? Math.Round(charged * Session.PricePerKWh, 2) : null;
                limits.Add(new LimitValue("cost", maxCost, Session.Currency, cost, Reached(cost, maxCost)));
            }

            if (limit.MaxTime is Int32 maxTime)
                limits.Add(new LimitValue("time", maxTime, "s", seconds, Reached(seconds, maxTime)));

            if (limit.MaxSoC is Int32 maxSoC)
                limits.Add(new LimitValue("soc", maxSoC, "%", null, null));

            return limits;

        }

        #endregion

        #region (static) Of(Session)

        /// <summary>
        /// How a session followed over its live link ended - or null while it
        /// did not, or where this phone stopped it for a wrong signed value.
        /// </summary>
        public static LiveLinkEnding? Of(ChargingSession Session)
        {

            if (Session.LiveLink is not LiveLinkFollow follow || follow.EndedAt is null || follow.Refused)
                return null;

            var limits   = LimitsOf(Session);
            var end      = follow.EndReading;
            var cause    = end?.Transaction is String transaction
                               ? TransactionInWords(transaction)
                               : "the station ended the stream of its live link, and its meter signed no end";

            var reached  = limits.Where(limit => limit.Reached == true).ToArray();
            var unknown  = limits.Where(limit => limit.Reached is null).ToArray();
            var notYet   = limits.Where(limit => limit.Reached == false).ToArray();

            static String Limits(IEnumerable<LimitValue> Limits)
                => String.Join(" and ", Limits.Select(limit => limit.LimitInWords));

            static String Values(IEnumerable<LimitValue> Limits)
                => String.Join(", ", Limits.Select(limit => limit.ValueInWords));

            String  reason;
            String  because;

            // How it ended, a wrong end value does not say.
            if (follow.WrongEnd is LiveLinkDocument wrongEnd)
                return new LiveLinkEnding("wrong",
                                          $"Its signed end meter value is a wrong one: {wrongEnd.Check!.Error} " +
                                          "How it ended, and against its limit, this phone takes nothing of it." +
                                          (follow.Stop is { Accepted: true } stoppedFirst
                                               ? $" This phone had stopped it through the stop link of the web payment page at {stoppedFirst.RequestedAt:HH:mm:ss}."
                                               : ""),
                                          null,
                                          limits);

            if (follow.Stop is { Accepted: true } stop)
            {
                reason   = "stopped";
                because  = $"This phone stopped it through the stop link of the web payment page at {stop.RequestedAt:HH:mm:ss}, and {cause}." +
                           (reached.Length > 0
                                ? $" Its limit of {Limits(reached)} was reached as well: {Values(reached)}."
                                : notYet.Length > 0
                                      ? $" Its limit of {Limits(notYet)} was not reached: {Values(notYet)}."
                                      : "");
            }

            else if (reached.Length > 0)
            {
                reason   = "limit";
                because  = $"It ended at its limit of {Limits(reached)}: {Values(reached)}, as its signed values say - {cause}.";
            }

            else if (limits.Count == 0)
            {
                reason   = "other";
                because  = $"It ended with no limit handed over for it: {cause}.";
            }

            else if (unknown.Length > 0)
            {
                reason   = "unknown";
                because  = (notYet.Length > 0 ? $"It ended before its limit of {Limits(notYet)} - {Values(notYet)}, as its signed values say. " : "It ended. ") +
                           $"Whether at its limit of {Limits(unknown)}, its signed values do not say: no meter measures how full the battery is. " +
                           $"{Capital(cause)}.";
            }

            else
            {
                reason   = "other";
                because  = $"It ended before its limit of {Limits(notYet)} was reached - {Values(notYet)}, as its signed values say: {cause}.";
            }

            return new LiveLinkEnding(reason, because, end?.Transaction, limits);

        }

        #endregion

        #region (static) TransactionInWords(Transaction)

        /// <summary>
        /// How a reading of an OCMF document says the transaction ended ("TX"),
        /// in words: its end, ended at the station, ended remotely, aborted,
        /// the power failed.
        /// </summary>
        public static String TransactionInWords(String Transaction)

            => Transaction switch {
                   "E"  => "its meter signed the end of the transaction (\"E\")",
                   "L"  => "its meter says it was ended at the station (\"L\")",
                   "R"  => "its meter says it was ended remotely (\"R\")",
                   "A"  => "its meter says it was aborted (\"A\")",
                   "P"  => "its meter says the power failed (\"P\")",
                   _    => $"its meter signed \"{Transaction}\" as the end of the transaction"
               };

        private static String Capital(String Text)
            => Text.Length > 0 ? Char.ToUpperInvariant(Text[0]) + Text[1..] : Text;

        #endregion

    }

}
