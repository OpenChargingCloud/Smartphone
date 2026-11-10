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

using NUnit.Framework;

using cloud.charging.open.Smartphone.Charging;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// What a session costs at a tariff of a roaming network, of every part it
    /// prices: energy, a flat fee, the time charging and the time not charging,
    /// each where its restrictions hold, rounded up to its step size, with its
    /// VAT, between the least and the most the tariff says.
    /// </summary>
    [TestFixture]
    public class TariffCostTests
    {

        #region Data

        /// <summary>
        /// Monday, 9 November 2026, 21:00 in Jena: 20:00 UTC.
        /// </summary>
        private static readonly DateTimeOffset Monday21 = new (2026, 11, 9, 20, 0, 0, TimeSpan.Zero);

        #endregion

        #region (private static) Component(...) / Element(...) / Tariff(...) / Linear(...)

        private static JObject Component(String Type, Decimal Price, Decimal? VAT = null, Int32? StepSize = null)
        {

            var json = new JObject(new JProperty("type", Type), new JProperty("price", Price));

            if (VAT      is Decimal vat)   json.Add(new JProperty("vat",      vat));
            if (StepSize is Int32   step)  json.Add(new JProperty("stepSize", step));

            return json;

        }

        private static JObject Element(JObject? Restrictions, params JObject[] Components)
        {

            var json = new JObject(new JProperty("priceComponents", new JArray(Components)));

            if (Restrictions is not null)
                json.Add(new JProperty("restrictions", Restrictions));

            return json;

        }

        private static SessionTariff Tariff(params JObject[] Elements)
            => new ("EUR", new JArray(Elements));

        /// <summary>
        /// A session that charged the given energy evenly from the given start
        /// for the given hours - and then nothing until the given end.
        /// </summary>
        private static ChargingProfile Linear(DateTimeOffset Start, Double Hours, Decimal KWh, DateTimeOffset? End = null)
            => new ([ (Start, 0m), (Start.AddHours(Hours), KWh) ], End);

        #endregion


        #region AnEnergyPriceAloneIsWhatThePhoneCountedWithBefore()

        [Test]
        public void AnEnergyPriceAloneIsWhatThePhoneCountedWithBefore()
        {

            var cost = TariffCost.Of(Tariff(Element(null, Component("ENERGY", 0.65m, StepSize: 1))), Linear(Monday21, 1, 20m));

            Assert.Multiple(() => {
                Assert.That(cost.Amount,                    Is.EqualTo(13.00m));
                Assert.That(cost.Exact,                     Is.True);
                Assert.That(cost.Notes,                     Is.Empty);
                Assert.That(cost.VATStated,                 Is.False, "no VAT is stated");
                Assert.That(cost.Components.Single().Dimension, Is.EqualTo("energy"));
                Assert.That(cost.Components.Single().Quantity,  Is.EqualTo(20m));
                Assert.That(cost.Limit,                     Is.Null);
            });

        }

        #endregion

        #region AFlatFeeAndVATAreCounted()

        [Test]
        public void AFlatFeeAndVATAreCounted()
        {

            var cost = TariffCost.Of(Tariff(Element(null, Component("FLAT", 1.00m, 19m), Component("ENERGY", 0.50m, 19m, 1))), Linear(Monday21, 1, 10m));

            Assert.Multiple(() => {
                Assert.That(cost.Amount,      Is.EqualTo(7.14m), "6.00 net and 19 % VAT");
                Assert.That(cost.VATStated,   Is.True);
                Assert.That(cost.Components.Select(component => component.Dimension), Is.EquivalentTo(new[] { "flat", "energy" }));
            });

        }

        #endregion

        #region TheTimeChargingIsCountedAfterTheDurationItsElementNames()

        [Test]
        public void TheTimeChargingIsCountedAfterTheDurationItsElementNames()
        {

            // 6.00 an hour of charging after four hours, and 0.50 a kWh.
            var tariff = Tariff(Element(new JObject(new JProperty("minDuration", 14400)), Component("TIME", 6.00m, StepSize: 60)),
                                Element(null,                                             Component("ENERGY", 0.50m)));

            var cost   = TariffCost.Of(tariff, Linear(Monday21, 5, 50m));

            Assert.Multiple(() => {
                Assert.That(cost.Amount, Is.EqualTo(31.00m), "25.00 for 50 kWh, and 6.00 for the fifth hour");
                Assert.That(cost.Components.Single(component => component.Dimension == "time").Quantity, Is.EqualTo(1m));
            });

        }

        #endregion

        #region TheTimeNotChargingIsCountedInItsStepSize()

        [Test]
        public void TheTimeNotChargingIsCountedInItsStepSize()
        {

            // 12.00 an hour parked, by the quarter of an hour, and 0.40 a kWh;
            // 20 minutes parked after charging.
            var tariff = Tariff(Element(null, Component("ENERGY", 0.40m), Component("PARKING_TIME", 12.00m, StepSize: 900)));
            var cost   = TariffCost.Of(tariff, Linear(Monday21, 1, 10m, Monday21.AddMinutes(80)));

            Assert.Multiple(() => {
                Assert.That(cost.Amount, Is.EqualTo(10.00m), "4.00 for 10 kWh, and half an hour parked, as 20 minutes are rounded up");
                Assert.That(cost.Components.Single(component => component.Dimension == "parking").Quantity, Is.EqualTo(0.5m));
            });

        }

        #endregion

        #region EnergyIsCountedInItsStepSize()

        [Test]
        public void EnergyIsCountedInItsStepSize()
        {

            var cost = TariffCost.Of(Tariff(Element(null, Component("ENERGY", 0.50m, StepSize: 1000))), Linear(Monday21, 1, 10.2m));

            Assert.That(cost.Amount, Is.EqualTo(5.50m), "11 kWh, as 10.2 kWh are rounded up to whole ones");

        }

        #endregion

        #region ATimeOfDayIsOfTheTimeZoneOfTheStation()

        [Test]
        public void ATimeOfDayIsOfTheTimeZoneOfTheStation()
        {

            // 0.30 a kWh from 22:00 to 06:00, 0.50 a kWh else; charged evenly
            // from 21:00 to 23:00 in Jena.
            var elements = new[] {
                               Element(new JObject(new JProperty("startTime", "22:00"), new JProperty("endTime", "06:00")), Component("ENERGY", 0.30m)),
                               Element(null,                                                                                Component("ENERGY", 0.50m))
                           };

            var inJena   = TariffCost.Of(Tariff(elements) with { TimeZone = "Europe/Berlin" }, Linear(Monday21, 2, 20m));
            var byOffset = TariffCost.Of(Tariff(elements) with { TimeZone = "UTC+01:00" },     Linear(Monday21, 2, 20m));
            var noZone   = TariffCost.Of(Tariff(elements),                                     Linear(Monday21, 2, 20m));

            Assert.Multiple(() => {

                Assert.That(inJena.Amount,    Is.EqualTo(8.00m), "10 kWh before 22:00 at 0.50, 10 kWh after at 0.30");
                Assert.That(inJena.Exact,     Is.True);
                Assert.That(inJena.Components.Count, Is.EqualTo(2));
                Assert.That(byOffset.Amount,  Is.EqualTo(8.00m));

                Assert.That(noZone.Amount,    Is.EqualTo(10.00m), "taken as UTC, all of it before 22:00");
                Assert.That(noZone.Exact,     Is.False);
                Assert.That(noZone.Notes.Single(), Does.Contain("names no time zone"));

            });

        }

        #endregion

        #region TheDaysOfTheWeekAreOfTheTimeZoneOfTheStation()

        [Test]
        public void TheDaysOfTheWeekAreOfTheTimeZoneOfTheStation()
        {

            var tariff = Tariff(Element(new JObject(new JProperty("daysOfWeek", new JArray("SATURDAY", "SUNDAY"))), Component("ENERGY", 0.20m)),
                                Element(null,                                                                    Component("ENERGY", 0.50m))) with { TimeZone = "Europe/Berlin" };

            var monday = TariffCost.Of(tariff, Linear(Monday21, 1, 10m));
            var sunday = TariffCost.Of(tariff, Linear(Monday21.AddDays(-1), 1, 10m));

            Assert.Multiple(() => {
                Assert.That(monday.Amount, Is.EqualTo(5.00m));
                Assert.That(sunday.Amount, Is.EqualTo(2.00m));
            });

        }

        #endregion

        #region TheEnergyAnElementNamesIsTheEnergyChargedSoFar()

        [Test]
        public void TheEnergyAnElementNamesIsTheEnergyChargedSoFar()
        {

            // The first 10 kWh at 0.40, the rest at 0.60.
            var tariff = Tariff(Element(new JObject(new JProperty("maxEnergy", 10000)), Component("ENERGY", 0.40m)),
                                Element(null,                                           Component("ENERGY", 0.60m)));

            var cost   = TariffCost.Of(tariff, Linear(Monday21, 1, 25m));

            Assert.That(cost.Amount, Is.EqualTo(13.00m), "4.00 for the first 10 kWh, 9.00 for the 15 after");

        }

        #endregion

        #region ThePowerAnElementNamesIsThePowerBetweenTwoValues()

        [Test]
        public void ThePowerAnElementNamesIsThePowerBetweenTwoValues()
        {

            var tariff = Tariff(Element(new JObject(new JProperty("minPower", 50000)), Component("ENERGY", 0.79m)),
                                Element(null,                                          Component("ENERGY", 0.49m)));

            var fast   = TariffCost.Of(tariff, Linear(Monday21, 1, 60m));
            var slow   = TariffCost.Of(tariff, Linear(Monday21, 1, 11m));

            Assert.Multiple(() => {
                Assert.That(fast.Amount, Is.EqualTo(47.40m), "60 kW");
                Assert.That(slow.Amount, Is.EqualTo(5.39m),  "11 kW");
            });

        }

        #endregion

        #region TheLeastAndTheMostATariffSaysHoldTheAmount()

        [Test]
        public void TheLeastAndTheMostATariffSaysHoldTheAmount()
        {

            var tariff = Tariff(Element(null, Component("ENERGY", 0.50m)));

            var least  = TariffCost.Of(tariff with { MinPrice = new JObject(new JProperty("includingVAT", 2.00m)) }, Linear(Monday21, 1, 1m));
            var most   = TariffCost.Of(tariff with { MaxPrice = new JObject(new JProperty("excludingVAT", 5.00m)) }, Linear(Monday21, 1, 20m));

            Assert.Multiple(() => {
                Assert.That(least.Amount,  Is.EqualTo(2.00m));
                Assert.That(least.Limit,   Is.EqualTo("minimum"));
                Assert.That(most.Amount,   Is.EqualTo(5.00m));
                Assert.That(most.Limit,    Is.EqualTo("maximum"));
            });

        }

        #endregion

        #region WhatNoMeterValueSaysMakesTheCostNotExact()

        [Test]
        public void WhatNoMeterValueSaysMakesTheCostNotExact()
        {

            var ofCurrent      = TariffCost.Of(Tariff(Element(new JObject(new JProperty("maxCurrent", 16)), Component("ENERGY", 0.30m)),
                                                      Element(null,                                         Component("ENERGY", 0.50m))),
                                               Linear(Monday21, 1, 10m));

            var pricedCurrent  = TariffCost.Of(Tariff(Element(null, Component("ENERGY", 0.50m), Component("MAX_CURRENT", 0.10m))), Linear(Monday21, 1, 10m));

            var reservation    = TariffCost.Of(Tariff(Element(new JObject(new JProperty("reservation", "RESERVATION")), Component("FLAT", 5.00m)),
                                                      Element(null,                                                     Component("ENERGY", 0.50m))),
                                               Linear(Monday21, 1, 10m));

            Assert.Multiple(() => {

                Assert.That(ofCurrent.Amount,       Is.EqualTo(5.00m), "taken as one that does not hold");
                Assert.That(ofCurrent.Exact,        Is.False);
                Assert.That(ofCurrent.Notes.Single(), Does.Contain("current"));

                Assert.That(pricedCurrent.Amount,   Is.EqualTo(5.00m));
                Assert.That(pricedCurrent.Exact,    Is.False);
                Assert.That(pricedCurrent.Notes.Single(), Does.Contain("MAX_CURRENT"));

                Assert.That(reservation.Amount,     Is.EqualTo(5.00m), "an element of a reservation prices no charging");
                Assert.That(reservation.Exact,      Is.True);

            });

        }

        #endregion

        #region NothingChargedCostsNothing()

        [Test]
        public void NothingChargedCostsNothing()
        {

            var cost = TariffCost.Of(Tariff(Element(null, Component("FLAT", 1.00m), Component("ENERGY", 0.50m))), new ChargingProfile([]));

            Assert.Multiple(() => {
                Assert.That(cost.Amount,      Is.Zero);
                Assert.That(cost.Components,  Is.Empty);
            });

        }

        #endregion

    }

}
