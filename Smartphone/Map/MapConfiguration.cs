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

#endregion

namespace cloud.charging.open.Smartphone.Map
{

    /// <summary>
    /// Where the map takes its charging stations from: the "map" section of
    /// the configuration file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For now one URL, and it is kept and shown and not yet asked: until it
    /// is, the map shows the demo stations of <see cref="DemoLocations"/>.
    /// Empty is the default and means exactly that.
    /// </para>
    /// <para>
    /// <code>
    /// "map": { "stationsURL": "https://emsp.example.org/api/v1/locations" }
    /// </code>
    /// </para>
    /// </remarks>
    /// <param name="StationsURL">Where the charging stations come from: an absolute http or https URL, or empty for none.</param>
    public sealed record MapConfiguration(String StationsURL)
    {

        #region Data

        /// <summary>
        /// The name of the section in the configuration file.
        /// </summary>
        public const String SectionName      = "map";

        /// <summary>
        /// The longest URL taken: longer is somebody who pasted the wrong
        /// thing, and no server answers it anyway.
        /// </summary>
        public const Int32  MaxURLLength     = 2048;

        /// <summary>
        /// What a smartphone starts with: no URL.
        /// </summary>
        public static readonly MapConfiguration Default = new ("");

        #endregion


        #region FromDocument(Document, out Configuration, out Error)

        /// <summary>
        /// The "map" section of the whole configuration document - or the
        /// default, where it has none.
        /// </summary>
        public static Boolean FromDocument(JObject                                     Document,
                                           [NotNullWhen(true)]  out MapConfiguration?  Configuration,
                                           [NotNullWhen(false)] out String?            Error)
        {

            if (!Document.TryGetValue(SectionName, out var section) || section.Type == JTokenType.Null)
            {
                Configuration  = Default;
                Error          = null;
                return true;
            }

            if (section is not JObject json)
            {
                Configuration  = null;
                Error          = $"The \"{SectionName}\" section of the configuration file is no JSON object.";
                return false;
            }

            return TryParse(json, out Configuration, out Error);

        }

        #endregion

        #region TryParse(JSON, out Configuration, out Error)

        /// <summary>
        /// A "map" section, or what a page sent in its shape.
        /// </summary>
        public static Boolean TryParse(JObject                                     JSON,
                                       [NotNullWhen(true)]  out MapConfiguration?  Configuration,
                                       [NotNullWhen(false)] out String?            Error)
        {

            Configuration = null;

            var token = JSON["stationsURL"];

            if (token is null || token.Type == JTokenType.Null)
            {
                Configuration  = Default;
                Error          = null;
                return true;
            }

            if (token.Type != JTokenType.String)
            {
                Error = "'stationsURL' is a URL in quotes, or empty.";
                return false;
            }

            var url = token.Value<String>()!.Trim();

            if (!IsAcceptable(url, out Error))
                return false;

            Configuration = new MapConfiguration(url);
            return true;

        }

        #endregion

        #region IsAcceptable(URL, out Error)

        /// <summary>
        /// Whether a URL may be where the map takes its stations from: empty,
        /// or an absolute http or https URL of a sensible length.
        /// </summary>
        public static Boolean IsAcceptable(String                            URL,
                                           [NotNullWhen(false)] out String?  Error)
        {

            Error = null;

            if (URL.Length == 0)
                return true;

            if (URL.Length > MaxURLLength)
                Error = $"The URL is {URL.Length} characters long; {MaxURLLength} are the most taken.";

            else if (!Uri.TryCreate(URL, UriKind.Absolute, out var uri))
                Error = $"'{URL}' is no absolute URL.";

            else if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                Error = $"'{URL}' is no http or https URL.";

            else if (String.IsNullOrEmpty(uri.Host))
                Error = $"'{URL}' names no host.";

            return Error is null;

        }

        #endregion

        #region ToJSON()

        /// <summary>
        /// The section, as the configuration file keeps it.
        /// </summary>
        public JObject ToJSON()

            => new (new JProperty("stationsURL", StationsURL));

        #endregion

    }

}
