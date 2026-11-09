import type { ChargingSession, ChargingSessionStatus, LimitValue, LiveLinkFollow, LiveLinkStartCheck } from '../api/client';

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
 * started says, in a line or two: how it is stopped - there, or here through
 * the stop link the page gave -, or that this phone stopped it, one of its
 * signed values a wrong one; what came over it, or how it ended against its
 * limit; how its signed values held against the proof of the station; and
 * how it is disputed, where it is.
 */
export function liveLinkInWords(Session: ChargingSession): string {

    const link = Session.liveLink;

    if (link === undefined)
        return '';

    if (link.refused)
        return `This phone stopped it: ${link.wrong !== null ? 'a signed meter value after its start' : 'its signed start meter value'} is a wrong one. ` +
               `${(link.wrong ?? link.start)?.error ?? ''} ${stationStopInWords(link)} ${disputeInWords(link)}`;

    const how  = link.stopLink === null
                     ? 'Started at the web payment page of its operator, and stopped there.'
                     : link.stop?.accepted === true && link.endedAt === null
                           ? `Started at the web payment page of its operator, which took the stop this phone asked for at ${clock(link.stop.requestedAt)}: it charges until the meter signs its end.`
                           : 'Started at the web payment page of its operator, and stopped through the stop link it gave.';

    const what = link.ending !== null
                     ? `It is over: ${link.ending.because}`
                     : link.endedBecause !== null
                           ? `It is over: ${link.endedBecause}`
                           : link.updates === 0
                                 ? link.problem ?? 'Nothing came over its live link yet.'
                                 : `${link.updates} update${link.updates === 1 ? '' : 's'} came over its live link, the last at ${clock(link.lastUpdateAt ?? '')}${link.lastTransport !== null ? ` over ${link.lastTransport}` : ''}.`;

    const limit = link.ending === null && link.limits.length > 0
                      ? ` Its limit: ${link.limits.map(one => one.value !== null ? `${limitFigure(one, one.limit)}, ${limitFigure(one, one.value)} so far` : limitFigure(one, one.limit)).join('; ')}.`
                      : '';

    const end = link.end?.holds === null
                    ? ` Its signed end value could not be checked: ${link.end.error ?? ''}`
                    : '';

    const dispute = link.disputeBecause !== null
                        ? ` ${disputeInWords(link)}`
                        : '';

    return `${how} ${what}${limit} ${startInWords(link.start)}${valuesInWords(link)}${end}${dispute}`;

}


/**
 * How a session this phone disputes is disputed, in a sentence: by this
 * phone, through the dispute link the web payment page gave - or there, by
 * the driver, where the page gave none or did not take the dispute. Nothing
 * where it is not disputed.
 */
export function disputeInWords(Link: LiveLinkFollow): string {

    if (Link.disputeBecause === null)
        return '';

    if (Link.dispute?.accepted === true)
        return `This phone disputed it through the dispute link of the web payment page at ${clock(Link.dispute.requestedAt)}.`;

    if (Link.dispute !== null)
        return `The web payment page did not take the dispute this phone sent: ${Link.dispute.answer} Dispute it there.`;

    if (Link.disputeLink !== null)
        return 'This phone disputes it through the dispute link of the web payment page.';

    return 'The web payment page of its operator gave this phone no dispute link: dispute it there.';

}


/**
 * How the station of a session this phone stopped for a wrong signed value
 * is stopped, in a sentence: by this phone, through the stop link the web
 * payment page gave - or there, by the driver, where the page gave none or
 * did not take the stop.
 */
export function stationStopInWords(Link: LiveLinkFollow): string {

    if (Link.stop?.accepted === true)
        return `This phone stopped the station through the stop link of the web payment page at ${clock(Link.stop.requestedAt)}.`;

    if (Link.stop !== null)
        return `The web payment page did not take the stop this phone asked for: ${Link.stop.answer} Stop it there.`;

    if (Link.stopLink !== null)
        return 'This phone stops the station through the stop link of the web payment page.';

    return 'The web payment page of its operator gave this phone no stop link: stop it there.';

}


/**
 * One part of the limit of a session, or what its signed values say of it,
 * in words: "20 kWh", "15,00 €", "1 h 30 min", "80 % state of charge".
 */
export function limitFigure(Limit: LimitValue, Figure: number): string {

    switch (Limit.kind) {
        case 'energy':  return `${Figure.toLocaleString(undefined, { maximumFractionDigits: 3 })} kWh`;
        case 'cost':    return money(Figure, Limit.unit);
        case 'time':    return duration(Figure);
        case 'soc':     return `${Figure.toLocaleString(undefined, { maximumFractionDigits: 0 })} % state of charge`;
    }

}


/**
 * How the signed start value of a live link held, in a sentence: the meter's,
 * as the proof of the station says, a wrong one, not checked, or not come
 * yet.
 */
export function startInWords(Start: LiveLinkStartCheck | null): string {

    if (Start === null)
        return 'No signed start value came over it yet; the figures are what its signed documents say.';

    const reading = Start.reading.energyKWh !== null
                        ? `, ${withUnit(Start.reading.energyKWh, 'kWh', 3)} at ${clock(Start.reading.measuredAt)},`
                        : '';

    switch (Start.holds) {
        case true:   return `Its signed start value${reading} is the meter's, by the key the proof of the station gives for it.`;
        case false:  return `Its signed start value${reading} is a wrong one: ${Start.error ?? ''}`;
        default:     return `Its signed start value${reading} could not be checked: ${Start.error ?? ''}`;
    }

}


/**
 * How the signed values after the start of a live link held, its end value
 * among them, in a sentence or two, each with a space before it: how many are
 * the meter's as well, how many its operator's, how many could not be checked,
 * and how many are not checked yet.
 */
export function valuesInWords(Link: LiveLinkFollow): string {

    const values      = Link.values;
    const byOperator  = values.byOperator ?? 0;
    const byMeter     = values.holding - byOperator;
    let   words       = '';

    if (byMeter > 0)
        words += ` ${byMeter} signed value${byMeter === 1 ? '' : 's'} after it ${byMeter === 1 ? 'is' : 'are'} the meter's as well, and fit${byMeter === 1 ? 's' : ''} the values before.`;

    if (byOperator > 0)
        words += ` ${byOperator} signed value${byOperator === 1 ? '' : 's'} between the start and the end ${byOperator === 1 ? 'is' : 'are'} its operator's, by the key the proof of the station gives it for its charge transparency records, and fit${byOperator === 1 ? 's' : ''} the values before: ${byOperator === 1 ? 'it shows' : 'they show'} how the session goes, and count${byOperator === 1 ? 's' : ''} for nothing.`;

    if (values.undecided > 0)
        words += ` ${values.undecided} could not be checked.`;

    if (values.notChecked > 0)
        words += ` ${values.notChecked} ${values.notChecked === 1 ? 'is' : 'are'} not checked yet.`;

    return words;

}


/** The class a session's status is coloured by. */
export function sessionStatusClass(Status: ChargingSessionStatus): string {
    return `session-${Status}`;
}


/** Where a session is, in one line: "Jena Holzmarkt, DE*SST*E2001*1". */
export function whereCharged(Session: ChargingSession): string {
    return `${Session.location.name}, ${Session.evse.evseId}`;
}
