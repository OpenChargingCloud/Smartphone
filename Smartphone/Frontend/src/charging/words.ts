import type { ChargingSession, ChargingSessionStatus } from '../api/client';

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


/** The class a session's status is coloured by. */
export function sessionStatusClass(Status: ChargingSessionStatus): string {
    return `session-${Status}`;
}


/** Where a session is, in one line: "Jena Holzmarkt, DE*SST*E2001*1". */
export function whereCharged(Session: ChargingSession): string {
    return `${Session.location.name}, ${Session.evse.evseId}`;
}
