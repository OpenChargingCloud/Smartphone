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
        case 'charging':  return 'charging';
        case 'stopped':   return 'stopped';
        case 'finished':  return 'finished';
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
