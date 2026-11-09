import type { ChargingSession, ChargingSessionStatus, LiveLinkStartCheck } from '../api/client';

/**
 * How a charging session is said on the pages that show one: its figures
 * with their units, how long it took, and what it is doing - one way, so that
 * the session open beside the history does not say it differently.
 */


/** A figure with its unit, in the browser's language: "11.2 kWh". */
export function withUnit(Value: number, Unit: string, Digits = 1): string {
    return `${Value.toLocaleString(undefined, { minimumFractionDigits: Digits, maximumFractionDigits: Digits })} ${Unit}`;
}


/** An amount of money in its currency: "4,90 €". */
export function money(Amount: number, Currency: string): string {
    try {
        return Amount.toLocaleString(undefined, { style: 'currency', currency: Currency });
    }
    catch {
        return `${Amount.toFixed(2)} ${Currency}`;
    }
}


/**
 * A time of day as a driver reads it, in the browser's language: "14:32" -
 * not the milliseconds the log keeps.
 */
export function clock(ISO: string): string {
    const time = new Date(ISO);
    return Number.isNaN(time.getTime()) ? ISO : time.toLocaleTimeString(undefined, { hour: '2-digit', minute: '2-digit' });
}


/** A day and a time of day, in the browser's language: "10.10.26, 14:32". */
export function dayAndTime(ISO: string): string {
    const time = new Date(ISO);
    return Number.isNaN(time.getTime()) ? ISO : time.toLocaleString(undefined, { dateStyle: 'short', timeStyle: 'short' });
}


/** A duration as a person says it: "1 h 05 min", "4 min 12 s". */
export function duration(Seconds: number): string {

    const total    = Math.max(0, Math.floor(Seconds));
    const hours    = Math.floor(total / 3600);
    const minutes  = Math.floor((total % 3600) / 60);
    const seconds  = total % 60;

    return hours > 0
               ? `${hours} h ${String(minutes).padStart(2, '0')} min`
               : `${minutes} min ${String(seconds).padStart(2, '0')} s`;

}


/** What a session is doing, as a person reads it. */
export function sessionStatusWord(Status: ChargingSessionStatus): string {
    switch (Status) {
        case 'starting':  return 'starting';
        case 'charging':  return 'charging';
        case 'stopped':   return 'stopped';
        case 'finished':  return 'finished';
        case 'failed':    return 'failed';
    }
}


/** Whether a session may still be stopped: starting or charging. */
export function isRunning(Session: ChargingSession): boolean {
    return Session.status === 'starting' || Session.status === 'charging';
}


/**
 * Where the figures of a session the e-mobility provider reported come from,
 * in a sentence: what the charge point operator told it, and when the phone
 * last heard of it.
 */
export function reportedInWords(Session: ChargingSession): string {

    if (Session.liveLink !== undefined)
        return liveLinkInWords(Session);

    const report = Session.provider;

    if (report === undefined)
        return '';

    const what = Session.status === 'failed'
                     ? `The charge point operator did not start it${report.result !== undefined && report.result !== 'ACCEPTED' ? `: it answered ${report.result}` : ''}.`
                     : Session.status === 'starting'
                           ? 'The charge point operator took the start, and has not reported charging yet.'
                           : report.cdr
                                 ? "The figures are the charge point operator's, from its charge detail record."
                                 : `The figures are the charge point operator's, as it reports the session${Session.status === 'charging' ? '' : ' - its charge detail record has not come yet'}.`;

    const cost = report.sessionStatus !== undefined && Session.cost.reported !== true
                     ? ' It says nothing of the cost: that is what was charged, times the price per kWh.'
                     : '';

    return `${what}${cost} Last heard from the e-mobility provider at ${clock(report.receivedAt)}.`;

}


/**
 * What the live link of a session the web payment page of its operator
 * started says, in a line or two: that it is stopped there - or that this
 * phone stopped it, its signed start value a wrong one -, what came over it,
 * and how its signed start value held against the proof of the station.
 */
export function liveLinkInWords(Session: ChargingSession): string {

    const link = Session.liveLink;

    if (link === undefined)
        return '';

    if (link.start?.holds === false)
        return `This phone stopped it: its signed start meter value is a wrong one. ${link.start.error ?? ''} ` +
               'The web payment page of its operator gives this phone no link to stop the station with: stop it there.';

    const what = link.endedBecause !== null
                     ? `It is over: ${link.endedBecause}`
                     : link.updates === 0
                           ? link.problem ?? 'Nothing came over its live link yet.'
                           : `${link.updates} update${link.updates === 1 ? '' : 's'} came over its live link, the last at ${clock(link.lastUpdateAt ?? '')}${link.lastTransport !== null ? ` over ${link.lastTransport}` : ''}.`;

    return `Started at the web payment page of its operator, and stopped there. ${what} ${startInWords(link.start)}`;

}


/**
 * How the signed start value of a live link held, in a sentence: the meter's,
 * as the proof of the station says - the values after it not checked yet -,
 * a wrong one, not checked, or not come yet.
 */
export function startInWords(Start: LiveLinkStartCheck | null): string {

    if (Start === null)
        return 'No signed start value came over it yet; the figures are what its signed documents say.';

    const reading = Start.reading !== null && Start.reading.energyKWh !== null
                        ? `, ${withUnit(Start.reading.energyKWh, 'kWh', 3)} at ${clock(Start.reading.measuredAt)},`
                        : '';

    switch (Start.holds) {
        case true:   return `Its signed start value${reading} is the meter's, by the key the proof of the station gives for it. The values after it are what its signed documents say, not checked yet.`;
        case false:  return `Its signed start value${reading} is a wrong one: ${Start.error ?? ''}`;
        default:     return `Its signed start value${reading} could not be checked: ${Start.error ?? ''}`;
    }

}


/** The class a session's status is coloured by. */
export function sessionStatusClass(Status: ChargingSessionStatus): string {
    return `session-${Status}`;
}


/** Where a session is, in one line: "Jena Holzmarkt, DE*SST*E2001*1". */
export function whereCharged(Session: ChargingSession): string {
    return `${Session.location.name}, ${Session.evse.evseId}`;
}
