/**
 * What this phone is charging, drawn in a document of happy-dom against a
 * stand-in smartphone: a card per running session with its figures, the one
 * just started marked, one the e-mobility provider reports without what OCPI
 * does not tell, Stop asking the smartphone, and a page with nothing charging
 * pointing at the map.
 */

import { asked, leave, open, refused, until, type Asked } from '../../test/smartphone.ts';
import { liveLinkSession, reportedSession, session, stopLinkSession, wrongStartSession, wrongValueSession } from '../../test/charging.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { chargingPage } = await import('./charging.ts');

after(() => leave());


let running = [ session(), session({ id: 'FFFFEEEEDDDD', location: { ...session().location, name: 'Lobeda Stadtteilzentrum' } }) ];
let stopAnswer: unknown = undefined;

function smartphone({ method, path, query }: Asked): unknown {

    if (method === 'GET' && path === '/charging/sessions' && query.get('active') === 'true')
        return { timestamp: '2026-10-10T08:10:00Z', simulated: running.length === 0 || running.some(one => one.simulated),
                 active: running.length, total: running.length + 3, sessions: running };

    if (method === 'POST' && path.endsWith('/stop'))
        return stopAnswer ?? { ...running[0], status: 'stopped' };

    return undefined;

}


describe('the charging page', () => {

    it('draws a card for every session that charges, the one just started marked', async () => {

        running    = [ session(), session({ id: 'FFFFEEEEDDDD', location: { ...session().location, name: 'Lobeda Stadtteilzentrum' } }) ];
        stopAnswer = undefined;

        const root = await open(chargingPage, '/charging?session=FFFFEEEEDDDD', [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelectorAll('.session-card').length === 2);

        const cards = [ ...root.querySelectorAll<HTMLElement>('.session-card') ];

        assert.match(cards[0]!.textContent!, /Jena Holzmarkt/);
        assert.match(cards[0]!.textContent!, /DE\*SST\*E2001\*1/);
        assert.match(cards[0]!.textContent!, /charging/);
        assert.equal(cards[0]!.classList.contains('just-started'), false);
        assert.equal(cards[1]!.classList.contains('just-started'), true, 'the session just started is not marked');

        assert.match(root.textContent!, /Simulated/);
        assert.match(root.textContent!, /5 in all/);

    });

    it('draws a session the provider reports without a power or a progress, saying whose figures they are', async () => {

        running    = [ reportedSession() ];
        stopAnswer = undefined;

        const root = await open(chargingPage, '/charging', [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-card .session-reported') !== null);

        const card = root.querySelector<HTMLElement>('.session-card')!;

        assert.match(card.textContent!, /4[.,]20 kWh/);
        assert.match(card.textContent!, /The figures are the charge point operator's/);
        assert.doesNotMatch(card.textContent!, /now|full at/);
        assert.equal(card.querySelector('progress'), null, 'a progress of what OCPI does not tell');
        assert.doesNotMatch(root.textContent!, /Simulated/);
        assert.ok(card.querySelector('.btn.danger'));

    });

    it('draws a session its operator\'s web payment page started without a Stop, saying it is stopped there', async () => {

        running    = [ liveLinkSession() ];
        stopAnswer = undefined;

        const root = await open(chargingPage, '/charging', [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelectorAll('.session-card').length === 1);

        const card = root.querySelector<HTMLElement>('.session-card')!;

        assert.match(card.textContent!, /Jena, Paradiesbahnhof/);
        assert.match(card.textContent!, /Started at the web payment page of its operator, and stopped there/);
        assert.match(card.textContent!, /7 updates came over its live link/);
        assert.match(card.textContent!, /Its signed start value, 1[.,]000[.,]000 kWh at [^,]+, is the meter's, by the key the proof of the station gives for it/);
        assert.match(card.textContent!, /1 signed value after it is the meter's as well/);
        assert.equal(card.querySelector('.btn.danger'), null, 'a Stop for a session stopped at the web payment page');

    });

    it('stops a session its operator\'s web payment page gave a stop link for, saying its limit', async () => {

        running    = [ stopLinkSession() ];
        stopAnswer = { ...stopLinkSession(), liveLink: { ...stopLinkSession().liveLink!,
                                                         stop: { requestedAt: '2026-10-10T08:11:00Z', requestedBy: 'driver1', accepted: true, answer: '200 OK: The station stops.' } } };

        const root = await open(chargingPage, '/charging', [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-card .btn.danger') !== null);

        const card = root.querySelector<HTMLElement>('.session-card')!;

        assert.match(card.textContent!, /Started at the web payment page of its operator, and stopped through the stop link it gave/);
        assert.match(card.textContent!, /Its limit: 20 kWh, 12[.,]5 kWh so far/);

        card.querySelector<HTMLButtonElement>('.btn.danger')!.click();

        await until(() => asked.some(one => one.method === 'POST' && one.path === '/charging/sessions/L1V3L1NK0001/stop'),
                    'Stop did not ask the smartphone');

    });

    it('says the phone stopped a session whose signed value after the start is a wrong one', async () => {

        running    = [ wrongValueSession() ];
        stopAnswer = undefined;

        const root = await open(chargingPage, '/charging', [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelectorAll('.session-card').length === 1);

        const card = root.querySelector<HTMLElement>('.session-card')!;

        assert.match(card.textContent!, /This phone stopped it: a signed meter value after its start is a wrong one/);
        assert.match(card.textContent!, /The meter counts backwards/);
        assert.match(card.textContent!, /stop it there/);
        assert.equal(card.querySelector('.btn.danger'), null, 'a Stop for a session the phone stopped');

    });

    it('says the phone stopped a session whose signed start value is a wrong one', async () => {

        running    = [ wrongStartSession() ];
        stopAnswer = undefined;

        const root = await open(chargingPage, '/charging', [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelectorAll('.session-card').length === 1);

        const card = root.querySelector<HTMLElement>('.session-card')!;

        assert.match(card.textContent!, /This phone stopped it: its signed start meter value is a wrong one/);
        assert.match(card.textContent!, /InvalidSignature/);
        assert.match(card.textContent!, /stop it there/);
        assert.equal(card.querySelector('.btn.danger'), null, 'a Stop for a session the phone stopped');

    });

    it('asks the smartphone to stop a session, and draws what it says then', async () => {

        running    = [ session() ];
        stopAnswer = undefined;

        const root = await open(chargingPage, '/charging', [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-card .btn.danger') !== null);

        running = [];

        root.querySelector<HTMLButtonElement>('.session-card .btn.danger')!.click();

        await until(() => asked.some(one => one.method === 'POST' && one.path === '/charging/sessions/A1B2C3D4E5F6/stop'),
                    'Stop did not ask the smartphone');

        await until(() => root.querySelector('.session-card') === null, 'the stopped session stayed');

        assert.match(root.textContent!, /Nothing is charging/);

    });

    it('says why a stop was refused, beside the session', async () => {

        running    = [ session() ];
        stopAnswer = refused(409, "The charging session 'A1B2C3D4E5F6' is finished already.");

        const root = await open(chargingPage, '/charging', [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-card .btn.danger') !== null);

        root.querySelector<HTMLButtonElement>('.session-card .btn.danger')!.click();

        await until(() => root.querySelector('.session-card .form-error') !== null, 'the refusal was not said');

        assert.match(root.querySelector('.session-card .form-error')!.textContent!, /finished already/);

    });

    it('points at the map where nothing charges, and offers no Stop to whoever may not', async () => {

        running    = [];
        stopAnswer = undefined;

        const root = await open(chargingPage, '/charging', [ 'charging:read' ],
                                smartphone, root => /Nothing is charging/.test(root.textContent!));

        assert.ok(root.querySelector('a[href$="/map"]'), 'there is no way to the map');
        assert.ok(root.querySelector('a[href$="/charging/history"]'), 'there is no way to the history');
        assert.match(root.textContent!, /may look at the charging sessions but not start or stop one/);

        running = [ session() ];

    });

});
