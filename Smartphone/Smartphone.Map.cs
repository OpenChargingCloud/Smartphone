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

using Newtonsoft.Json.Linq;

using cloud.charging.open.Smartphone.Map;

#endregion

namespace cloud.charging.open.Smartphone
{

    public partial class Smartphone
    {

        #region Data

        private readonly Object    mapLock = new();
        private MapConfiguration   map;

        #endregion

        #region Properties

        /// <summary>
        /// Where the map takes its charging stations from.
        /// </summary>
        public MapConfiguration Map
        {
            get
            {
                lock (mapLock)
                    return map;
            }
        }

        #endregion


        #region (private) LoadMapConfiguration()

        /// <summary>
        /// The "map" section the configuration file had at the start - or no
        /// data source, said in the log, where it was not one.
        /// </summary>
        private MapConfiguration LoadMapConfiguration()
        {

            if (MapConfiguration.FromDocument(ConfigurationDocument, out var configuration, out var problem))
                return configuration;

            Log.Warning($"The map's section of the configuration file was not taken, and the map has no data source: {problem}", "map", "configuration");

            return MapConfiguration.Default;

        }

        #endregion

        #region TryUpdateMapConfiguration(JSON, Who, out Error, out NotSaved)

        /// <summary>
        /// Point the map at another source of charging stations - or at none.
        /// </summary>
        /// <param name="JSON">What the page sent, in the shape of the "map" section.</param>
        /// <param name="Who">Who asked, for the log.</param>
        /// <param name="Error">Why nothing was changed.</param>
        /// <param name="NotSaved">True where the configuration file could not be read or written: nothing about the change was wrong, and nothing was changed.</param>
        public Boolean TryUpdateMapConfiguration(JObject                           JSON,
                                                 String                            Who,
                                                 [NotNullWhen(false)] out String?  Error,
                                                 out Boolean                       NotSaved)
        {

            NotSaved = false;

            if (!MapConfiguration.TryParse(JSON, out var configuration, out Error))
                return false;

            lock (mapLock)
            {

                if (!ConfigFile.TryMergeSection(MapConfiguration.SectionName, configuration.ToJSON(), out Error))
                {
                    NotSaved = true;
                    return false;
                }

                var before = map;

                map = configuration;

                if (before.StationsURL != configuration.StationsURL)
                    Log.Notice(configuration.StationsURL.Length > 0
                                   ? $"'{Who}' at the web interface set the map's data source to '{configuration.StationsURL}'. It is kept, and not asked yet: the map still shows its demo stations."
                                   : $"'{Who}' at the web interface removed the map's data source.",
                               "map", "configuration", "web");

                return true;

            }

        }

        #endregion

        #region MapConfigurationJSON()

        /// <summary>
        /// Where the map takes its stations from, as Configuration → Map shows
        /// it.
        /// </summary>
        public JObject MapConfigurationJSON()
        {

            var configuration = Map;

            return new JObject(
                       new JProperty("stationsURL",   configuration.StationsURL),
                       new JProperty("source",        "demo"),
                       new JProperty("maxURLLength",  MapConfiguration.MaxURLLength),
                       new JProperty("file",          ConfigFile.Path)
                   );

        }

        #endregion


        #region StatusOf(Location, EVSE, Now)

        /// <summary>
        /// The status an EVSE has now, as the map shows it: charging where this
        /// phone charges there, and otherwise what the EVSE says of itself.
        /// </summary>
        public String StatusOf(ChargingLocation  Location,
                               ChargingEVSE      EVSE,
                               DateTimeOffset    Now)

            => Sessions.IsCharging(Location.Id, EVSE.Uid, Now)
                   ? EVSEStatus.Charging
                   : DemoLocations.StatusOf(EVSE, Now);

        #endregion

        #region LocationsJSON()

        /// <summary>
        /// Every charging station of the map, with the status of each EVSE
        /// now, and where they came from.
        /// </summary>
        public JObject LocationsJSON()
        {

            var now = TimeProvider.GetUtcNow();

            return new JObject(
                       new JProperty("source",       "demo"),
                       new JProperty("stationsURL",  Map.StationsURL),
                       new JProperty("tiles",        $"{MapTileServer}/{{z}}/{{x}}/{{y}}.png"),
                       new JProperty("timestamp",    now.ToString("o")),
                       new JProperty("statuses",     new JArray(EVSEStatus.All)),
                       new JProperty("locations",    new JArray(DemoLocations.All.Select(location => location.ToJSON(evse => StatusOf(location, evse, now), now))))
                   );

        }

        #endregion

    }

}
