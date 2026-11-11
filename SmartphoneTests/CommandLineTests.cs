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

using NUnit.Framework;

using org.GraphDefined.Vanaheimr.CLI;
using org.GraphDefined.Vanaheimr.Hermod;

using cloud.charging.open.protocols.WWCP.Node.CommandLine;
using cloud.charging.open.protocols.WWCP.Node.Configuration;
using cloud.charging.open.protocols.WWCP.Node.TestKit;

using cloud.charging.open.Smartphone.CommandLine;

#endregion

namespace cloud.charging.open.Smartphone.Tests
{

    /// <summary>
    /// The command line of a smartphone: the smartphone's own, SmartphoneCLI,
    /// whichever program runs the smartphone - over SSH as well as at the
    /// console.
    /// </summary>
    /// <remarks>
    /// There is no command yet that only a smartphone can be told; what a
    /// smartphone has is the node's. What is tested here is that the command
    /// line is the smartphone's all the same - handed out by the smartphone
    /// itself, about this smartphone, with every command of the node - and
    /// that a command of the smartphone's, once there is one, is on it: the
    /// test environment runs a smartphone without the program SmartphoneCLI,
    /// as every test here does.
    /// </remarks>
    public class CommandLineTests
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


        #region OverSSHTheSmartphoneHandsOutItsOwnCommandLine()

        /// <summary>
        /// Whoever signs in over SSH gets the command line of the smartphone,
        /// about this smartphone, without the program that runs it having to
        /// say so.
        /// </summary>
        [Test]
        public async Task OverSSHTheSmartphoneHandsOutItsOwnCommandLine()
        {

            Assert.That(smartphone!.CommandLines, Is.Not.Null, "a session over SSH would get the node's command line only");

            await using var terminal  = new VT100Terminal((bytes, cancellationToken) => ValueTask.CompletedTask);
            using       var cli       = smartphone.CommandLines!(terminal, CLICaller.Console);

            Assert.That(cli,                                  Is.InstanceOf<SmartphoneCLI>());
            Assert.That(((SmartphoneCLI) cli).Smartphone,     Is.SameAs(smartphone));

            Assert.That((await cli.Execute("who")).Single(),  Is.EqualTo($"Nobody is connected to this {smartphone.Kind.Name} over SSH."));

        }

        #endregion

        #region AtTheConsoleTheSmartphonesCommandLineAnswersAsOverSSH()

        /// <summary>
        /// The command line the program SmartphoneCLI runs at the console is
        /// the same, and answers the same.
        /// </summary>
        [Test]
        public async Task AtTheConsoleTheSmartphonesCommandLineAnswersAsOverSSH()
        {

            using var cli = new SmartphoneCLI(smartphone!);

            Assert.That(cli.Smartphone,                                Is.SameAs(smartphone));
            Assert.That((await cli.Execute("who")).Single(),           Is.EqualTo($"Nobody is connected to this {smartphone!.Kind.Name} over SSH."));
            Assert.That((await cli.Execute("noSuchCommand")).Single(), Does.StartWith("Unknown command"));

        }

        #endregion

        #region EveryCommandOfTheNodeAndOfTheSmartphoneIsOnIt()

        /// <summary>
        /// Every command of the node is on the smartphone's command line, and
        /// so is every command of the smartphone's assembly - which is found
        /// only where it is built from a SmartphoneCLI, or from nothing: a
        /// command built from anything else would be on no command line, and
        /// nothing else would say so.
        /// </summary>
        [Test]
        public async Task EveryCommandOfTheNodeAndOfTheSmartphoneIsOnIt()
        {

            await using var terminal  = new VT100Terminal((bytes, cancellationToken) => ValueTask.CompletedTask);
            using       var cli       = smartphone!.CommandLines!(terminal, CLICaller.Console);

            var onIt              = cli.Commands.Select(command => command.GetType()).ToHashSet();

            var ofTheNode         = typeof(NodeCLI).Assembly.GetTypes().
                                        Where(type => typeof(ICLICommand).IsAssignableFrom(type) &&
                                                     !type.IsAbstract &&
                                                     !type.IsInterface &&
                                                      type.GetConstructor([ typeof(NodeCLI) ]) is not null).
                                        ToArray();

            var ofTheSmartphone   = typeof(Smartphone).Assembly.GetTypes().
                                        Where(type => typeof(ICLICommand).IsAssignableFrom(type) &&
                                                     !type.IsAbstract &&
                                                     !type.IsInterface).
                                        ToArray();

            Assert.That(ofTheNode, Is.Not.Empty, "the node has commands - syncNTS among them");

            foreach (var command in ofTheNode.Concat(ofTheSmartphone))
                Assert.That(onIt, Does.Contain(command), $"{command.Name} is not on the smartphone's command line");

        }

        #endregion

    }

}
