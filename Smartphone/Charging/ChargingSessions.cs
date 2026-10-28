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

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using cloud.charging.open.Smartphone.Map;
using cloud.charging.open.Smartphone.Provider;

#endregion

namespace cloud.charging.open.Smartphone.Charging
{

    /// <summary>
    /// Every charging session of this phone, running and past, kept in one
    /// file beside the configuration file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One file and not a directory: a phone has a few hundred sessions in
    /// its life, not millions, and the history page wants all of them at
    /// once. It is written whole, to a file beside it first and then moved
    /// over it, so that a crash halfway through leaves the old one rather than
    /// half of a new one.
    /// </para>
    /// <para>
    /// A file that cannot be read is moved aside with the time in its name and
    /// said so, instead of being written over by the next session started: the
    /// sessions in it are somebody's history, and an empty list is no reason
    /// to throw it away.
    /// </para>
    /// </remarks>
    public sealed class ChargingSessions
    {

        #region Data

        /// <summary>
        /// What the file is called beside the configuration file, unless
        /// another is given.
        /// </summary>
        public const String DefaultFileName = "charging-sessions.json";

        private readonly Object                 sync      = new();
        private readonly List<ChargingSession>  sessions  = [];
        private readonly TimeProvider           timeProvider;

        #endregion

        #region Properties

        /// <summary>
        /// The file the sessions are kept in.
        /// </summary>
        public String               FilePath     { get; }

        /// <summary>
        /// What went wrong while the file was read - a file moved aside, a
        /// session that could not be read - for the log to say once.
        /// </summary>
        public IReadOnlyList<String> ReadProblems { get; }

        #endregion

        #region Constructor(s)

        /// <summary>
        /// The sessions kept in the given file - none, where there is none yet.
        /// </summary>
        public ChargingSessions(String         FilePath,
                                TimeProvider?  TimeProvider = null)
        {

            this.FilePath      = Path.GetFullPath(FilePath);
            this.timeProvider  = TimeProvider ?? TimeProvider.System;

            var problems = new List<String>();

            Load(problems);

            this.ReadProblems  = problems;

        }

        #endregion


        #region All() / Active() / Get(Id)

        /// <summary>
        /// Every session, the newest first.
        /// </summary>
        public IReadOnlyList<ChargingSession> All()
        {
            lock (sync)
                return [.. sessions.OrderByDescending(session => session.StartedAt)];
        }

        /// <summary>
        /// The sessions still charging at the given time, the newest first.
        /// </summary>
        public IReadOnlyList<ChargingSession> Active(DateTimeOffset Now)

            => [.. All().Where(session => session.IsActiveAt(Now))];

        /// <summary>
        /// One session, by its id.
        /// </summary>
        public ChargingSession? Get(String Id)
        {
            lock (sync)
                return sessions.FirstOrDefault(session => session.Id == Id);
        }

        #endregion

        #region IsCharging(LocationId, EVSEUid, Now)

        /// <summary>
        /// Whether this phone is charging at the given EVSE at the given time.
        /// </summary>
        public Boolean IsCharging(String          LocationId,
                                  String          EVSEUid,
                                  DateTimeOffset  Now)
        {
            lock (sync)
                return sessions.Any(session => session.LocationId == LocationId &&
                                               session.EVSEUid    == EVSEUid    &&
                                               session.IsActiveAt(Now));
        }

        #endregion

        #region TryStart(Location, EVSE, StatusOf, StartedBy, Ticket, Start, out Session, out Error, out NotSaved)

        /// <summary>
        /// Start a session at an EVSE of a location - where it is available, or
        /// its status is not known.
        /// </summary>
        /// <param name="Location">Where.</param>
        /// <param name="EVSE">At which of its EVSEs.</param>
        /// <param name="StatusOf">What an EVSE says of itself now, before this phone is taken into account.</param>
        /// <param name="StartedBy">Who started it.</param>
        /// <param name="Ticket">The charging ticket it is started with, where the phone has an e-mobility provider.</param>
        /// <param name="Start">Its start, as the provider countersigned it.</param>
        /// <param name="Session">The session started.</param>
        /// <param name="Error">Why none was.</param>
        /// <param name="NotSaved">True where the file could not be written: nothing about the request was wrong, and nothing was started.</param>
        public Boolean TryStart(ChargingLocation                           Location,
                                ChargingEVSE                               EVSE,
                                Func<ChargingEVSE, DateTimeOffset, String> StatusOf,
                                String                                     StartedBy,
                                ChargingTicketReference?                   Ticket,
                                ChargingStartReference?                    Start,
                                [NotNullWhen(true)]  out ChargingSession?  Session,
                                [NotNullWhen(false)] out String?           Error,
                                out Boolean                                NotSaved)
        {

            Session   = null;
            NotSaved  = false;

            lock (sync)
            {

                var now = timeProvider.GetUtcNow();

                if (sessions.Any(session => session.LocationId == Location.Id && session.EVSEUid == EVSE.Uid && session.IsActiveAt(now)))
                {
                    Error = $"This phone is already charging at {EVSE.EVSEId}.";
                    return false;
                }

                var status = StatusOf(EVSE, now);

                // Unknown is what an EVSE of a roaming network is, which says
                // its status only over its WebSocket: started at as at an
                // available one, as a driver standing in front of it would.
                if (status != EVSEStatus.Available && status != EVSEStatus.Unknown)
                {
                    Error = $"{EVSE.EVSEId} at {Location.Name} is {EVSEStatus.InWords(status)}, not available.";
                    return false;
                }

                var started = ChargingSession.New(NewId(), Location, EVSE, now, StartedBy, Ticket, Start);

                sessions.Add(started);

                if (!TrySave(out Error))
                {
                    sessions.Remove(started);
                    NotSaved = true;
                    return false;
                }

                Session = started;
                return true;

            }

        }

        #endregion

        #region TryStop(Id, StoppedBy, Stopped, out Session, out Error, out NotFound, out NotSaved)

        /// <summary>
        /// Stop a session that is still charging - with its start as the
        /// provider countersigned the stop, where it was started through one.
        /// </summary>
        public Boolean TryStop(String                                     Id,
                               String                                     StoppedBy,
                               ChargingStartReference?                    Stopped,
                               [NotNullWhen(true)]  out ChargingSession?  Session,
                               [NotNullWhen(false)] out String?           Error,
                               out Boolean                                NotFound,
                               out Boolean                                NotSaved)
        {

            Session   = null;
            NotFound  = false;
            NotSaved  = false;

            lock (sync)
            {

                var now    = timeProvider.GetUtcNow();
                var index  = sessions.FindIndex(session => session.Id == Id);

                if (index < 0)
                {
                    NotFound  = true;
                    Error     = $"There is no charging session '{Id}' on this phone.";
                    return false;
                }

                var running = sessions[index];

                if (!running.IsActiveAt(now))
                {
                    Error = $"The charging session '{Id}' is {running.StatusAt(now).ToString().ToLowerInvariant()} already.";
                    return false;
                }

                var stopped = running with { StoppedAt = now, StoppedBy = StoppedBy, Start = Stopped ?? running.Start };

                sessions[index] = stopped;

                if (!TrySave(out Error))
                {
                    sessions[index] = running;
                    NotSaved = true;
                    return false;
                }

                Session = stopped;
                return true;

            }

        }

        #endregion


        #region (private) NewId()

        /// <summary>
        /// A new session's id: short enough to be read out, long enough not to
        /// be met twice.
        /// </summary>
        private String NewId()
        {

            String id;

            do
                id = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            while (sessions.Any(session => session.Id == id));

            return id;

        }

        #endregion

        #region (private) Load(Problems)

        private void Load(List<String> Problems)
        {

            if (!File.Exists(FilePath))
                return;

            JArray array;

            try
            {

                using var reader = new JsonTextReader(new StringReader(File.ReadAllText(FilePath))) {
                                       // Times stay text, with their offsets, as
                                       // they were written - not DateTimes in
                                       // whatever zone the machine is in.
                                       DateParseHandling = DateParseHandling.None
                                   };

                array = JToken.ReadFrom(reader) as JArray
                            ?? throw new JsonException("it holds no JSON array.");

            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
            {

                var aside = $"{FilePath}.unreadable-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}";

                try
                {
                    File.Move(FilePath, aside);
                    Problems.Add($"The charging sessions in '{FilePath}' could not be read ({e.Message}); the file was moved to '{aside}', and this phone starts with none.");
                }
                catch (Exception moveProblem)
                {
                    Problems.Add($"The charging sessions in '{FilePath}' could not be read ({e.Message}), and the file could not be moved aside either ({moveProblem.Message}).");
                }

                return;

            }

            foreach (var item in array)
            {

                if (item is not JObject json)
                    Problems.Add("An entry of the charging sessions is no JSON object, and was left out.");

                else if (ChargingSession.TryParseStorage(json, out var session, out var error))
                    sessions.Add(session);

                else
                    Problems.Add($"{error} It was left out.");

            }

            // The next session started writes the file anew, without what was
            // left out: a copy of it as it was keeps that.
            if (Problems.Count > 0)
            {

                var copy = $"{FilePath}.unreadable-{timeProvider.GetUtcNow():yyyyMMdd-HHmmss}";

                try
                {
                    File.Copy(FilePath, copy, overwrite: false);
                    Problems.Add($"The file as it was is kept as '{copy}'.");
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    Problems.Add($"The file as it was could not be copied to '{copy}': {e.Message}");
                }

            }

        }

        #endregion

        #region (private) TrySave(out Error)

        private Boolean TrySave([NotNullWhen(false)] out String? Error)
        {

            Error = null;

            try
            {

                Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? ".");

                var temporary = FilePath + ".tmp";

                File.WriteAllText(temporary, new JArray(sessions.Select(session => session.ToStorageJSON())).ToString(Formatting.Indented));
                File.Move(temporary, FilePath, overwrite: true);

                return true;

            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Error = $"The charging sessions could not be written to '{FilePath}': {e.Message}";
                return false;
            }

        }

        #endregion

    }

}
