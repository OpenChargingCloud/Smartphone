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

using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.Certificates;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// What kind of node a smartphone is: its names, its port, the
    /// certificates it keeps, and what its Configuration page leads with.
    /// </summary>
    public class SmartphoneTests
    {

        #region Data

        private const String  NoTimeServers  = """{ "nts": { "groups": { "legal": { "enabled": false } } } }""";

        private String       directory   = "";
        private Smartphone?  smartphone;

        #endregion

        #region Setup / TearDown

        [SetUp]
        public void Setup()
        {
            directory = Path.Combine(Path.GetTempPath(), "smartphone-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(directory);
        }

        [TearDown]
        public async Task TearDown()
        {

            if (smartphone is not null)
                await smartphone.DisposeAsync();

            try
            {
                if (Directory.Exists(directory))
                    Directory.Delete(directory, recursive: true);
            }
            catch (Exception)
            {
                // A temporary directory that outlives one test run is not worth
                // failing the run over.
            }

        }

        #endregion


        #region (helper) SmartphoneFrom()

        /// <summary>
        /// A smartphone whose time client is switched off, on a free port of
        /// the loopback - made, and not yet started.
        /// </summary>
        private Smartphone SmartphoneFrom()
        {

            var file = Path.Combine(directory, WWCPConfigFile.DefaultFileName);

            File.WriteAllText(file, NoTimeServers);

            smartphone = new Smartphone(
                             HTTPPort:          IPPort.Parse(TestPorts.Free()),
                             AccountsPath:      Path.Combine(directory, "accounts"),
                             ConfigFile:        new WWCPConfigFile(file),
                             CertificatesPath:  Path.Combine(directory, "certificates"),
                             LogToConsole:      false,
                             BridgeDebugLog:    false
                         );

            return smartphone;

        }

        #endregion


        #region TheKind()

        /// <summary>
        /// The names a smartphone goes by - and the organization of its
        /// accounts above all, which a first start writes down for good.
        /// </summary>
        [Test]
        public void TheKind()
        {

            var kind = Smartphone.SmartphoneKind;

            Assert.Multiple(() => {
                Assert.That(kind.Name,             Is.EqualTo("smartphone"));
                Assert.That(kind.CapitalisedName,  Is.EqualTo("Smartphone"));
                Assert.That(kind.Tag,              Is.EqualTo("smartphone"));
                Assert.That(kind.Product,          Is.EqualTo("Smartphone"));
                Assert.That(kind.Organization,     Is.EqualTo("Smartphone"));
                Assert.That(kind.LogFilePrefix,    Is.EqualTo("smartphone"));
            });

        }

        #endregion

        #region ThePort()

        /// <summary>
        /// One below the vehicle, and clear of every sibling's.
        /// </summary>
        [Test]
        public void ThePort()
        {
            Assert.That(Smartphone.DefaultHTTPPort, Is.EqualTo(IPPort.Parse(2346)));
        }

        #endregion

        #region WhatItKeeps()

        /// <summary>
        /// The roots it believes, the server certificates it holds a server
        /// to and who it is as a client - and neither a root of clients nor
        /// anything of ISO 15118.
        /// </summary>
        [Test]
        public void WhatItKeeps()
        {

            var store = SmartphoneFrom().Certificates;

            Assert.Multiple(() => {

                Assert.That(store.Kinds, Is.EqualTo(new[] {
                                                        CertificateKind.TLSRoot,
                                                        CertificateKind.TLSServer,
                                                        CertificateKind.TLSIdentity
                                                    }));

                Assert.That(store.Kinds, Does.Not.Contain(CertificateKind.ClientRoot));
                Assert.That(store.Kinds.Intersect(CertificateKindExtensions.ISO15118), Is.Empty);

            });

        }

        #endregion

        #region TheConfigurationLeadsWithTheSmartphone()

        /// <summary>
        /// The card the Configuration page leads with is the smartphone's.
        /// </summary>
        [Test]
        public void TheConfigurationLeadsWithTheSmartphone()
        {

            var json  = SmartphoneFrom().ConfigurationJSON();
            var first = json.Properties().First();

            Assert.Multiple(() => {
                Assert.That(first.Name,                                 Is.EqualTo("smartphone"));
                Assert.That(first.Value["version"]?.Value<String>(),    Is.Not.Empty);
                Assert.That(first.Value["machine"]?.Value<String>(),    Is.EqualTo(Environment.MachineName));
            });

        }

        #endregion

    }

}
