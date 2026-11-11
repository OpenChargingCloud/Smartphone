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
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

using org.GraphDefined.Vanaheimr.Illias;

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// The tariff a session is charged by, as the proof of its station and the
    /// price statement in force at its start give it - or as the operator of
    /// the station agreed it for the session: its currency, its elements as
    /// WWCP_POI writes them - each of price components of energy, a flat fee,
    /// the time charging and the time not charging, and of restrictions when
    /// it applies -, the least and the most a session costs, and the time zone
    /// of the station, which the times of day of its restrictions are of.
    /// </summary>
    /// <param name="Currency">The currency of its prices.</param>
    /// <param name="Elements">Its elements, as WWCP_POI writes them.</param>
    /// <param name="TariffId">The charging tariff it is.</param>
    /// <param name="MinPrice">The least a session costs, where it says one: "excludingVAT", "includingVAT".</param>
    /// <param name="MaxPrice">The most a session costs, where it says one.</param>
    /// <param name="TimeZone">The time zone of the station: an IANA or Windows id, or an offset as "UTC+01:00".</param>
    public sealed record SessionTariff(String    Currency,
                                       JArray    Elements,
                                       String?   TariffId  = null,
                                       JObject?  MinPrice  = null,
                                       JObject?  MaxPrice  = null,
                                       String?   TimeZone  = null)
    {

        #region ToJSON() / TryParse(JSON)

        public JObject ToJSON()
        {

            var json = new JObject(
                           new JProperty("currency",  Currency),
                           new JProperty("elements",  Elements.DeepClone())
                       );

            if (TariffId is not null)  json.Add(new JProperty("tariffId",  TariffId));
            if (MinPrice is not null)  json.Add(new JProperty("minPrice",  MinPrice.DeepClone()));
            if (MaxPrice is not null)  json.Add(new JProperty("maxPrice",  MaxPrice.DeepClone()));
            if (TimeZone is not null)  json.Add(new JProperty("timeZone",  TimeZone));

            return json;

        }

        /// <summary>
        /// A tariff as the store kept it, or null where it kept none.
        /// </summary>
        public static SessionTariff? TryParse(JToken? JSON)

            => JSON is JObject json && json.Value<String>("currency") is String currency && json["elements"] is JArray elements
                   ? new SessionTariff(currency,
                                       (JArray) elements.DeepClone(),
                                       json.Value<String>("tariffId"),
                                       json["minPrice"] as JObject,
                                       json["maxPrice"] as JObject,
                                       json.Value<String>("timeZone"))
                   : null;

        #endregion

    }


    /// <summary>
    /// What a session charged over time, as its cost is worked out of it: the
    /// energy charged since its start, in kWh, at each time known - the first
    /// point its start -, and until when it counts, where that is later than
    /// the last point: the time after it is time not charging.
    /// </summary>
    /// <param name="Points">The energy charged since the start, in kWh, at each time known, in time order.</param>
    /// <param name="End">Until when the session counts, where later than its last point.</param>
    public sealed record ChargingProfile(IReadOnlyList<(DateTimeOffset At, Decimal EnergyKWh)>  Points,
                                         DateTimeOffset?                                        End  = null);


    /// <summary>
    /// One part of the cost of a session: of which element of its tariff, what
    /// it priced - "energy", "flat", "time" charging or "parking", the time
    /// not charging -, how much of it - in kWh, hours, or once -, rounded up to
    /// the step size of the component, at which price and VAT, and what it
    /// comes to with VAT.
    /// </summary>
    public sealed record CostComponent(Int32     Element,
                                       String    Dimension,
                                       Decimal   Quantity,
                                       String    Unit,
                                       Decimal   Price,
                                       Decimal?  VAT,
                                       Decimal   Amount)
    {

        public JObject ToJSON()

            => new (new JProperty("element",    Element),
                    new JProperty("dimension",  Dimension),
                    new JProperty("quantity",   Math.Round(Quantity, 6)),
                    new JProperty("unit",       Unit),
                    new JProperty("price",      Price),
                    new JProperty("vat",        VAT),
                    new JProperty("amount",     Math.Round(Amount, 4)));

    }


    /// <summary>
    /// The cost of a session as its tariff makes it of what it charged when,
    /// worked out as OCPI and WWCP_POI price a session: for every stretch of
    /// time, of each dimension - the energy, the time charging, the time not
    /// charging - the price of the first element of the tariff whose
    /// restrictions hold then that prices it, a flat fee of the first element
    /// that holds at the start, each part rounded up to its step size, with its
    /// VAT, and the whole held between the least and the most the tariff says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Restrictions of the time of day, the date and the day of the week are
    /// of the local time of the station; of energy and of duration, of what
    /// the session charged and how long it ran until then; of power, of the
    /// power between two points known. A restriction of current no meter value
    /// says, and an element with it is taken as one that does not hold: the
    /// cost is then not exact (<see cref="Exact"/>), and why is said
    /// (<see cref="Notes"/>) - as it is of a component of current, which is
    /// priced at nothing, and of times of day where the time zone of the
    /// station is not known, which are taken as UTC. An element of a
    /// reservation prices none.
    /// </para>
    /// <para>
    /// A price without VAT is taken as it is: whether it is a gross price or a
    /// net one is not said (<see cref="VATStated"/>).
    /// </para>
    /// </remarks>
    /// <param name="Currency">The currency of the tariff.</param>
    /// <param name="Amount">What it costs, with VAT where stated, rounded to the cent.</param>
    /// <param name="Components">Its parts.</param>
    /// <param name="Exact">Whether everything of the tariff could be decided.</param>
    /// <param name="Notes">Why not, where it could not.</param>
    /// <param name="VATStated">Whether every component it priced with stated its VAT.</param>
    /// <param name="Limit">"minimum" or "maximum", where the least or the most the tariff says made the amount.</param>
    public sealed partial record TariffCost(String                        Currency,
                                            Decimal                       Amount,
                                            IReadOnlyList<CostComponent>  Components,
                                            Boolean                       Exact,
                                            IReadOnlyList<String>         Notes,
                                            Boolean                       VATStated,
                                            String?                       Limit)
    {

        #region Data

        private sealed record Component(Int32     Index,
                                        String    Type,
                                        Decimal   Price,
                                        Decimal?  VAT,
                                        Decimal?  StepSize);

        private sealed record Restrictions(TimeOnly?             StartTime,
                                           TimeOnly?             EndTime,
                                           DateOnly?             StartDate,
                                           DateOnly?             EndDate,
                                           Decimal?              MinEnergy,
                                           Decimal?              MaxEnergy,
                                           Decimal?              MinPower,
                                           Decimal?              MaxPower,
                                           Decimal?              MinDuration,
                                           Decimal?              MaxDuration,
                                           HashSet<DayOfWeek>?   DaysOfWeek,
                                           Boolean               OfCurrent,
                                           Boolean               OfReservation)
        {

            public Boolean OfTimes
                => StartTime is not null || EndTime is not null || StartDate is not null || EndDate is not null || DaysOfWeek is not null;

        }

        private sealed record Element(Int32                       Index,
                                      IReadOnlyList<Component>    Components,
                                      Restrictions                Restrictions);

        /// <summary>
        /// The state of a session at one time, as restrictions ask it.
        /// </summary>
        private readonly record struct State(DateTime  Local,
                                             Decimal   DurationSeconds,
                                             Decimal   EnergyWh,
                                             Decimal   PowerW);

        [GeneratedRegex(@"^(?:UTC|GMT)?\s*([+-])(\d{1,2})(?::?(\d{2}))?$", RegexOptions.IgnoreCase)]
        private static partial Regex OffsetZone();

        #endregion


        #region ToJSON()

        public JObject ToJSON()

            => new (new JProperty("currency",    Currency),
                    new JProperty("amount",      Amount),
                    new JProperty("components",  new JArray(Components.Select(component => component.ToJSON()))),
                    new JProperty("exact",       Exact),
                    new JProperty("notes",       new JArray(Notes)),
                    new JProperty("vatStated",   VATStated),
                    new JProperty("limit",       Limit));

        #endregion

        #region (static) Of(Tariff, Profile)

        /// <summary>
        /// What the given profile costs at the given tariff.
        /// </summary>
        public static TariffCost Of(SessionTariff    Tariff,
                                    ChargingProfile  Profile)
        {

            var notes     = new List<String>();
            var exact     = true;

            void Note(String Note)
            {
                exact = false;
                if (!notes.Contains(Note))
                    notes.Add(Note);
            }

            var elements  = ElementsOf(Tariff.Elements, Note);
            var zone      = ZoneOf(Tariff.TimeZone);

            if (zone is null && elements.Any(element => element.Restrictions.OfTimes))
                Note(Tariff.TimeZone is null
                         ? "The tariff names times of day, and its station names no time zone: they are taken as UTC."
                         : $"The tariff names times of day, and the time zone of its station, '{Tariff.TimeZone}', is none this phone knows: they are taken as UTC.");

            foreach (var element in elements.Where(element => element.Restrictions.OfCurrent))
                Note($"Its element {element.Index + 1} applies at a current no meter value says: it is taken as one that does not.");

            var points = Profile.Points.OrderBy(point => point.At).ToArray();

            if (points.Length == 0)
                return new TariffCost(Tariff.Currency, 0m, [], exact, notes, true, null);

            var start     = points[0].At;
            var end       = Profile.End is DateTimeOffset until && until > points[^1].At ? until : points[^1].At;

            // The stretches between the points known, and after the last one
            // until the end: no energy, so time not charging.
            var stretches = new List<(DateTimeOffset From, DateTimeOffset To, Decimal FromKWh, Decimal ToKWh)>();

            for (var i = 1; i < points.Length; i++)
                if (points[i].At > points[i - 1].At)
                    stretches.Add((points[i - 1].At, points[i].At, points[i - 1].EnergyKWh, Math.Max(points[i - 1].EnergyKWh, points[i].EnergyKWh)));

            var charged = stretches.Count > 0 ? stretches[^1].ToKWh : points[^1].EnergyKWh;

            if (end > points[^1].At)
                stretches.Add((points[^1].At, end, charged, charged));

            // Where a restriction changes whether it holds: cut there.
            var cuts = CutsOf(elements, stretches, start, end, zone);

            var quantities  = new Dictionary<(Int32 Element, Int32 Component), Decimal>();

            // At the start: nothing charged, no time gone, at the power of
            // the first stretch.
            var atStart     = new State(Local(start, zone), 0, 0,
                                        stretches.Count > 0 && stretches[0].To > stretches[0].From
                                            ? (stretches[0].ToKWh - stretches[0].FromKWh) / (Decimal) (stretches[0].To - stretches[0].From).TotalHours * 1000m
                                            : 0m);

            void Add(Element Element, Component Component, Decimal Quantity)
            {
                var key = (Element.Index, Component.Index);
                quantities[key] = quantities.GetValueOrDefault(key) + Quantity;
            }

            foreach (var (from, to, fromKWh, toKWh) in stretches)
            {

                var inner = cuts.Where(cut => cut > from && cut < to).OrderBy(cut => cut).ToList();
                var times = new List<DateTimeOffset> { from };
                times.AddRange(inner);
                times.Add(to);

                var hours = (Decimal) (to - from).TotalHours;
                var power = hours > 0 ? (toKWh - fromKWh) / hours * 1000m : 0m;

                for (var i = 1; i < times.Count; i++)
                {

                    var a       = times[i - 1];
                    var b       = times[i];
                    var share   = (Decimal) ((b - a).TotalHours / (to - from).TotalHours);
                    var aKWh    = fromKWh + (toKWh - fromKWh) * (Decimal) ((a - from).TotalHours / (to - from).TotalHours);
                    var kWh     = (toKWh - fromKWh) * share;
                    var middle  = a + (b - a) / 2;

                    var state   = new State(Local(middle, zone),
                                            (Decimal) (middle - start).TotalSeconds,
                                            (aKWh + kWh / 2) * 1000m,
                                            power);

                    var length  = (Decimal) (b - a).TotalHours;

                    if (kWh > 0 && Priced(elements, "ENERGY", state) is (Element energyElement, Component energy))
                        Add(energyElement, energy, kWh);

                    if (kWh > 0 && Priced(elements, "TIME", state) is (Element timeElement, Component time))
                        Add(timeElement, time, length);

                    if (kWh <= 0 && Priced(elements, "PARKING_TIME", state) is (Element parkingElement, Component parking))
                        Add(parkingElement, parking, length);

                }

            }

            // A flat fee: of the first element that holds at the start.
            if (Priced(elements, "FLAT", atStart) is (Element flatElement, Component flat))
                Add(flatElement, flat, 1m);

            var components = new List<CostComponent>();

            foreach (var ((elementIndex, componentIndex), raw) in quantities.OrderBy(entry => entry.Key.Element).ThenBy(entry => entry.Key.Component))
            {

                var component  = elements[elementIndex].Components.First(component => component.Index == componentIndex);

                var (quantity, unit) = component.Type switch {
                    "ENERGY"        => (Stepped(raw * 1000m, component.StepSize) / 1000m, "kWh"),
                    "TIME"          => (Stepped(raw * 3600m, component.StepSize) / 3600m,  "h"),
                    "PARKING_TIME"  => (Stepped(raw * 3600m, component.StepSize) / 3600m,  "h"),
                    _               => (1m,                                                  "")
                };

                var net    = quantity * component.Price;
                var gross  = component.VAT is Decimal vat ? net * (1m + vat / 100m) : net;

                components.Add(new CostComponent(elementIndex + 1,
                                                 component.Type switch {
                                                     "ENERGY"        => "energy",
                                                     "TIME"          => "time",
                                                     "PARKING_TIME"  => "parking",
                                                     _               => "flat"
                                                 },
                                                 quantity,
                                                 unit,
                                                 component.Price,
                                                 component.VAT,
                                                 gross));

            }

            var amount  = Math.Round(components.Sum(component => component.Amount), 2, MidpointRounding.AwayFromZero);
            String? limit = null;

            if (PriceOf(Tariff.MinPrice) is Decimal min && amount < min)
            {
                amount  = min;
                limit   = "minimum";
            }

            if (PriceOf(Tariff.MaxPrice) is Decimal max && amount > max)
            {
                amount  = max;
                limit   = "maximum";
            }

            return new TariffCost(Tariff.Currency,
                                  amount,
                                  components,
                                  exact,
                                  notes,
                                  components.All(component => component.VAT is not null),
                                  limit);

        }

        #endregion


        #region (private static) ElementsOf(Elements, Note)

        /// <summary>
        /// The elements of a tariff as WWCP_POI writes them, each with the
        /// components that price something this phone can work out.
        /// </summary>
        private static IReadOnlyList<Element> ElementsOf(JArray          Elements,
                                                         Action<String>  Note)
        {

            var elements = new List<Element>();

            foreach (var (json, index) in Elements.Select((element, index) => (element as JObject, index)))
            {

                if (json is null)
                {
                    Note($"Its element {index + 1} is none: it prices nothing.");
                    elements.Add(new Element(index, [], RestrictionsOf(null)));
                    continue;
                }

                var components = new List<Component>();

                foreach (var (component, componentIndex) in (json["priceComponents"] as JArray ?? []).Select((component, componentIndex) => (component as JObject, componentIndex)))
                {

                    var type = component?["type"]?.Type == JTokenType.String ? component["type"]!.Value<String>()! : "";

                    if (component is null || !TryNumber(component["price"], out var price))
                    {
                        Note($"A price component of its element {index + 1} names no price: it prices nothing.");
                        continue;
                    }

                    if (type is not ("ENERGY" or "FLAT" or "TIME" or "PARKING_TIME"))
                    {
                        Note($"A price component of its element {index + 1} prices '{type}', which no meter value says: it is priced at nothing.");
                        continue;
                    }

                    components.Add(new Component(componentIndex,
                                                 type,
                                                 price,
                                                 TryNumber(component["vat"],      out var vat)      ? vat      : null,
                                                 TryNumber(component["stepSize"], out var stepSize) ? stepSize : null));

                }

                elements.Add(new Element(index, components, RestrictionsOf(json["restrictions"] as JObject)));

            }

            return elements;

        }

        private static Restrictions RestrictionsOf(JObject? JSON)
        {

            Decimal? Number(String Name)
                => JSON is not null && TryNumber(JSON[Name], out var number) ? number : null;

            TimeOnly? Time(String Name)
                => JSON?[Name]?.Type == JTokenType.String && TimeOnly.TryParseExact(JSON[Name]!.Value<String>(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : null;

            DateOnly? Date(String Name)
                => JSON?[Name]?.Type == JTokenType.String && DateOnly.TryParseExact(JSON[Name]!.Value<String>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

            var days = JSON?["daysOfWeek"] is JArray daysOfWeek
                           ? daysOfWeek.Values<String>().
                                        Select(day => Enum.TryParse<DayOfWeek>(day, true, out var parsed) ? parsed : (DayOfWeek?) null).
                                        OfType<DayOfWeek>().
                                        ToHashSet()
                           : null;

            return new Restrictions(Time  ("startTime"),
                                    Time  ("endTime"),
                                    Date  ("startDate"),
                                    Date  ("endDate"),
                                    Number("minEnergy"),
                                    Number("maxEnergy"),
                                    Number("minPower"),
                                    Number("maxPower"),
                                    Number("minDuration"),
                                    Number("maxDuration"),
                                    days,
                                    JSON?["minCurrent"] is not null || JSON?["maxCurrent"] is not null,
                                    JSON?["reservation"] is not null);

        }

        #endregion

        #region (private static) CutsOf(Elements, Stretches, Start, End, Zone)

        /// <summary>
        /// The times between the start and the end where a restriction may
        /// change whether it holds: the start and the end of every time of day
        /// it names, every local midnight - of its dates and days -, the time a
        /// duration it names is reached, and the time an energy it names is.
        /// </summary>
        private static HashSet<DateTimeOffset> CutsOf(IReadOnlyList<Element>                                                       Elements,
                                                      IReadOnlyList<(DateTimeOffset From, DateTimeOffset To, Decimal FromKWh, Decimal ToKWh)>  Stretches,
                                                      DateTimeOffset                                                               Start,
                                                      DateTimeOffset                                                               End,
                                                      TimeZoneInfo?                                                                Zone)
        {

            var cuts          = new HashSet<DateTimeOffset>();
            var restrictions  = Elements.Select(element => element.Restrictions).ToArray();

            if (restrictions.Any(restriction => restriction.OfTimes))
            {

                var timesOfDay = restrictions.SelectMany(restriction => new[] { restriction.StartTime, restriction.EndTime }).
                                              OfType<TimeOnly>().
                                              Append(TimeOnly.MinValue).
                                              Distinct().
                                              ToArray();

                for (var day = DateOnly.FromDateTime(Local(Start, Zone)).AddDays(-1); day <= DateOnly.FromDateTime(Local(End, Zone)).AddDays(1); day = day.AddDays(1))
                    foreach (var timeOfDay in timesOfDay)
                        if (Utc(day.ToDateTime(timeOfDay), Zone) is DateTimeOffset cut)
                            cuts.Add(cut);

            }

            foreach (var restriction in restrictions)
            {

                foreach (var seconds in new[] { restriction.MinDuration, restriction.MaxDuration }.OfType<Decimal>())
                    cuts.Add(Start + TimeSpan.FromSeconds((Double) seconds));

                foreach (var wh in new[] { restriction.MinEnergy, restriction.MaxEnergy }.OfType<Decimal>())
                {

                    var kWh = wh / 1000m;

                    foreach (var (from, to, fromKWh, toKWh) in Stretches)
                        if (fromKWh < kWh && kWh <= toKWh)
                            cuts.Add(from + (to - from) * (Double) ((kWh - fromKWh) / (toKWh - fromKWh)));

                }

            }

            return cuts;

        }

        #endregion

        #region (private static) Priced(Elements, Type, State) / Holds(Restrictions, State)

        /// <summary>
        /// The first element whose restrictions hold in the given state that
        /// prices the given dimension, and its component that does.
        /// </summary>
        private static (Element, Component)? Priced(IReadOnlyList<Element>  Elements,
                                                    String                  Type,
                                                    State                   State)
        {

            foreach (var element in Elements)
                if (element.Components.FirstOrDefault(component => component.Type == Type) is Component component &&
                    Holds(element.Restrictions, State))
                    return (element, component);

            return null;

        }

        private static Boolean Holds(Restrictions  Restriction,
                                     State         State)
        {

            if (Restriction.OfReservation || Restriction.OfCurrent)
                return false;

            var time = TimeOnly.FromDateTime(State.Local);
            var date = DateOnly.FromDateTime(State.Local);

            if (Restriction.StartTime is TimeOnly startTime && Restriction.EndTime is TimeOnly endTime)
            {
                if (startTime < endTime ? time < startTime || time >= endTime
                                        : time < startTime && time >= endTime)
                    return false;
            }
            else if (Restriction.StartTime is TimeOnly onlyStart && time < onlyStart)
                return false;
            else if (Restriction.EndTime   is TimeOnly onlyEnd   && time >= onlyEnd)
                return false;

            return !(Restriction.StartDate   is DateOnly startDate    && date                  <  startDate    ||
                     Restriction.EndDate     is DateOnly endDate      && date                  >= endDate      ||
                     Restriction.DaysOfWeek  is HashSet<DayOfWeek> d  && !d.Contains(State.Local.DayOfWeek)   ||
                     Restriction.MinEnergy   is Decimal minEnergy     && State.EnergyWh        <  minEnergy    ||
                     Restriction.MaxEnergy   is Decimal maxEnergy     && State.EnergyWh        >= maxEnergy    ||
                     Restriction.MinPower    is Decimal minPower      && State.PowerW          <  minPower     ||
                     Restriction.MaxPower    is Decimal maxPower      && State.PowerW          >= maxPower     ||
                     Restriction.MinDuration is Decimal minDuration   && State.DurationSeconds <  minDuration  ||
                     Restriction.MaxDuration is Decimal maxDuration   && State.DurationSeconds >= maxDuration);

        }

        #endregion

        #region (private static) ZoneOf(TimeZone) / Local(Time, Zone) / Utc(Local, Zone)

        /// <summary>
        /// The time zone of the given id - IANA or Windows - or offset, as
        /// "UTC+01:00" - or null where it is none known.
        /// </summary>
        private static TimeZoneInfo? ZoneOf(String? TimeZone)
        {

            if (String.IsNullOrWhiteSpace(TimeZone))
                return null;

            var match = OffsetZone().Match(TimeZone.Trim());

            if (match.Success)
            {

                var offset = new TimeSpan(Int32.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                                          match.Groups[3].Success ? Int32.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture) : 0,
                                          0);

                return TimeZoneInfo.CreateCustomTimeZone(TimeZone.Trim(), match.Groups[1].Value == "-" ? -offset : offset, TimeZone.Trim(), TimeZone.Trim());

            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZone.Trim());
            }
            catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return null;
            }

        }

        private static DateTime Local(DateTimeOffset  Time,
                                      TimeZoneInfo?   Zone)

            => Zone is not null
                   ? TimeZoneInfo.ConvertTime(Time, Zone).DateTime
                   : Time.UtcDateTime;

        private static DateTimeOffset? Utc(DateTime       Local,
                                           TimeZoneInfo?  Zone)
        {

            if (Zone is null)
                return new DateTimeOffset(Local, TimeSpan.Zero);

            // A local time a change of the clock skips is none.
            if (Zone.IsInvalidTime(Local))
                return null;

            return new DateTimeOffset(Local, Zone.GetUtcOffset(Local));

        }

        #endregion

        #region (private static) Stepped(Quantity, StepSize) / PriceOf(Limit) / TryNumber(JSON, out Number)

        /// <summary>
        /// The given quantity, in the unit of the step size, rounded up to it.
        /// </summary>
        private static Decimal Stepped(Decimal   Quantity,
                                       Decimal?  StepSize)

            => StepSize is Decimal step && step > 0
                   ? Math.Ceiling(Math.Round(Quantity, 6) / step) * step
                   : Quantity;

        /// <summary>
        /// The least or the most a session costs, with VAT where the tariff
        /// says it, else without.
        /// </summary>
        private static Decimal? PriceOf(JObject? Limit)

            => Limit is not null && TryNumber(Limit["includingVAT"], out var including)
                   ? including
                   : Limit is not null && TryNumber(Limit["excludingVAT"], out var excluding)
                         ? excluding
                         : null;

        private static Boolean TryNumber(JToken?      JSON,
                                         out Decimal  Number)
        {

            Number = 0;

            if (JSON is not JValue { Type: JTokenType.Integer or JTokenType.Float } value)
                return false;

            Number = value.AsDecimal();
            return true;

        }

        #endregion

    }

}
