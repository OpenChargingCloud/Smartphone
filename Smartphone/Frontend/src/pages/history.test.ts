/**
 * Every charging session of this phone, drawn in a document of happy-dom
 * against a stand-in smartphone: a row each, the newest first as the
 * smartphone sends them, and each row leading to the session.
 */

import { leave, open, type Asked } from '../../test/smartphone.ts';
import { session } from '../../test/charging.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { historyPage } = await import('./history.ts');

after(() => leave());


let sessions = [
    session(),
    session({ id: 'OLDONE000001', status: 'finished', startedAt: '2026-10-09T18:00:00Z', endedAt: '2026-10-09T20:43:38Z', energyKWh: 30, powerKW: 0, cost: { amount: 14.7, pricePerKWh: 0.49, currency: 'EUR' } }),
    session({ id: 'OLDONE000002', status: 'stopped',  startedAt: '2026-10-08T07:00:00Z', endedAt: '2026-10-08T07:10:00Z', energyKWh: 1.833, powerKW: 0, stoppedBy: 'root' })
];

function smartphone({ method, path }: Asked): unknown {

    if (method === 'GET' && path === '/charging/sessions')
        return { timestamp: '2026-10-10T08:10:00Z', simulated: true, active: sessions.filter(one => one.status === 'charging').length, total: sessions.length, sessions };

    return undefined;

}


describe('the charging history', () => {

    it('draws a row for every session, with what it is doing, and a link to it', async () => {

        const root = await open(historyPage, '/charging/history', [ 'charging:read' ],
                                smartphone, root => root.querySelectorAll('table.session-table tbody tr').length === 3);

        const rows = [ ...root.querySelectorAll<HTMLTableRowElement>('table.session-table tbody tr') ];

        assert.deepEqual(rows.map(row => row.querySelector('.session-status')!.textContent!.trim()),
                         [ 'charging', 'finished', 'stopped' ]);

        assert.ok(rows[1]!.querySelector('a[href$="/charging/sessions/OLDONE000001"]'), 'a row does not lead to its session');
        assert.ok(root.querySelector('.table-scroll > table.session-table'), 'the table does not scroll inside its card');
        assert.match(root.textContent!, /3 session\(s\)/);
        assert.match(root.textContent!, /1 charging now/);

    });

    it('points at the map where there is no session yet', async () => {

        sessions = [];

        const root = await open(historyPage, '/charging/history', [ 'charging:read' ],
                                smartphone, root => /No sessions yet/.test(root.textContent!));

        assert.ok(root.querySelector('a[href$="/map"]'));

    });

});
