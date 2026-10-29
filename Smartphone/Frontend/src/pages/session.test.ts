/**
 * One charging session, drawn in a document of happy-dom against a stand-in
 * smartphone: what it charged and where, Stop while it starts or charges and
 * not after, one the e-mobility provider reports with the operator's figures
 * and without what OCPI does not tell, and a session that is not there said
 * so.
 */

import { asked, leave, open, refused, until, type Asked } from '../../test/smartphone.ts';
import { reportedSession, session } from '../../test/charging.ts';

import { strict as assert }    from 'node:assert';
import { after, describe, it } from 'node:test';

const { sessionPage } = await import('./session.ts');

after(() => leave());


let current = session();

function smartphone({ method, path }: Asked): unknown {

    if (method === 'GET' && path === `/charging/sessions/${current.id}`)
        return current;

    if (method === 'POST' && path === `/charging/sessions/${current.id}/stop`) {
        current = { ...current, status: 'stopped', endedAt: '2026-10-10T08:10:00Z', powerKW: 0, stoppedBy: 'root' };
        return current;
    }

    if (method === 'GET' && path.startsWith('/charging/sessions/'))
        return refused(404, `There is no charging session '${path.split('/').pop()}' on this phone.`);

    return undefined;

}


describe('a charging session', () => {

    it('says where, what and how much, and stops on Stop', async () => {

        current = session();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.btn.danger') !== null);

        assert.match(root.textContent!, /Jena Holzmarkt/);
        assert.match(root.textContent!, /DE\*SST\*E2001\*1/);
        assert.match(root.textContent!, /Type 2/);
        assert.match(root.textContent!, /Started by\s*root/);
        assert.ok(root.querySelector('progress.session-progress'));

        root.querySelector<HTMLButtonElement>('.btn.danger')!.click();

        await until(() => asked.some(one => one.method === 'POST' && one.path === `/charging/sessions/${current.id}/stop`),
                    'Stop did not ask the smartphone');
        await until(() => root.querySelector('.btn.danger') === null, 'Stop stayed after the session was over');

        assert.match(root.querySelector('.session-status')!.textContent!, /stopped/);
        assert.match(root.textContent!, /Stopped by\s*root/);

    });

    it('names the charging ticket it was started with, and nothing secret of it', async () => {

        current = session({ status: 'stopped', endedAt: '2026-10-10T08:10:00Z', stoppedBy: 'root',
                            ticket: { id: '9f2c41d08a7e3b65c0ffee0123456789', party: 'DE*GDF',
                                      notBefore: '2026-10-10T08:00:00Z', notAfter: '2026-10-10T09:00:00Z',
                                      keyThumbprint: '6cab95a41a13b2c42f552b7c81248078b3f319afe7d3caf133bc4b4dbead1605',
                                      issuerFingerprint: '0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0' } });

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read' ],
                                smartphone, root => root.querySelector('.session-ticket') !== null);

        const ticket = root.querySelector('.session-ticket')!.textContent!;

        assert.match(ticket, /9f2c41d08a7e3b65c0ffee0123456789/);
        assert.match(ticket, /DE\*GDF/);
        assert.match(ticket, /6cab95a41a13b2c42f552b7c81248078b3f319afe7d3caf133bc4b4dbead1605/);
        assert.match(root.textContent!, /the e-mobility provider issued its ticket/);
        assert.equal(root.querySelector('.session-start'), null, 'a start through the provider nobody made');

    });

    it('names the start and the stop the provider countersigned', async () => {

        current = session({ status: 'stopped', endedAt: '2026-10-10T08:10:00Z', stoppedBy: 'root',
                            ticket: { id: '9f2c41d08a7e3b65c0ffee0123456789', party: 'DE*GDF',
                                      notBefore: '2026-10-10T08:00:00Z', notAfter: '2026-10-10T09:00:00Z',
                                      keyThumbprint: '6cab95a41a13b2c42f552b7c81248078b3f319afe7d3caf133bc4b4dbead1605',
                                      issuerFingerprint: '0f1e2d3c4b5a69788796a5b4c3d2e1f00f1e2d3c4b5a69788796a5b4c3d2e1f0' },
                            start:  { token: 'T-9F2C41D08A7E3B65', authorizationReference: '8e1c0ffee0123456789abcdef0123456',
                                      requestDigest: 'a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90',
                                      countersignedAt: '2026-10-10T08:00:02Z',
                                      stopRequestDigest: 'ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100',
                                      stopCountersignedAt: '2026-10-10T08:10:01Z' } });

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read' ],
                                smartphone, root => root.querySelector('.session-start') !== null);

        const start = root.querySelector('.session-start')!.textContent!;

        assert.match(start, /T-9F2C41D08A7E3B65/);
        assert.match(start, /8e1c0ffee0123456789abcdef0123456/);
        assert.match(start, /a1b2c3d4e5f60718293a4b5c6d7e8f90a1b2c3d4e5f60718293a4b5c6d7e8f90/);
        assert.match(start, /ffeeddccbbaa99887766554433221100ffeeddccbbaa99887766554433221100/);
        assert.match(root.textContent!, /Started through the e-mobility provider, which countersigned the start/);
        assert.doesNotMatch(root.textContent!, /nothing was charged for real/);

    });

    it("shows a session the provider reports with the operator's figures, and no power, target or time it is full at", async () => {

        current = reportedSession();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-reported') !== null);

        const text = root.textContent!;

        assert.match(root.querySelector('.session-status')!.textContent!, /charging/);
        assert.match(text, /as the operator reports it/);
        assert.match(text, /The figures are the charge point operator's, as it reports the session\./);
        assert.match(text, /Charging since/);
        assert.doesNotMatch(text, /Power/);
        assert.doesNotMatch(text, /Progress|Full at/);
        assert.doesNotMatch(text, /simulated/i);
        assert.equal(root.querySelector('progress.session-progress'), null, 'a progress of what OCPI does not tell');
        assert.ok(root.querySelector('.btn.danger'), 'no Stop for a session that charges');

    });

    it('offers Stop while the operator has not reported charging yet', async () => {

        current = reportedSession({ status: 'starting', energyKWh: 0, durationSeconds: 0,
                                    cost: { amount: 0, pricePerKWh: 0.49, currency: 'EUR', reported: false },
                                    provider: { state: 'started', receivedAt: '2026-10-10T08:00:05Z', cdr: false } });

        delete current.chargingSince;

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-reported') !== null);

        assert.match(root.querySelector('.session-status')!.textContent!, /starting/);
        assert.match(root.textContent!, /has not reported charging yet/);
        assert.doesNotMatch(root.textContent!, /says nothing of the cost/, 'no cost to say nothing of before there is a session');
        assert.ok(root.querySelector('.btn.danger'), 'no Stop for a session that starts');

    });

    it('says why a start failed, and offers no Stop', async () => {

        current = reportedSession({ status: 'failed', energyKWh: 0, durationSeconds: 0, endedAt: '2026-10-10T08:00:09Z',
                                    cost: { amount: 0, pricePerKWh: 0.49, currency: 'EUR', reported: false },
                                    provider: { state: 'failed', receivedAt: '2026-10-10T08:00:10Z', cdr: false, result: 'EVSE_OCCUPIED' } });

        delete current.chargingSince;

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-reported') !== null);

        assert.match(root.querySelector('.session-status')!.textContent!, /failed/);
        assert.match(root.textContent!, /did not start it: it answered EVSE_OCCUPIED/);
        assert.match(root.textContent!, /Ended/);
        assert.equal(root.querySelector('.btn.danger'), null, 'Stop for a start that failed');

    });

    it('says the figures are of the charge detail record once it came, and its cost where the operator says none', async () => {

        current = reportedSession({ status: 'finished', energyKWh: 12.5, endedAt: '2026-10-10T09:30:00Z',
                                    cost: { amount: 6.13, pricePerKWh: 0.49, currency: 'EUR', reported: false },
                                    provider: { state: 'ended', receivedAt: '2026-10-10T09:31:00Z', cdr: true, result: 'ACCEPTED', sessionStatus: 'COMPLETED' } });

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-reported') !== null);

        assert.match(root.textContent!, /from its charge detail record/);
        assert.match(root.textContent!, /says nothing of the cost: that is what was charged, times the price per kWh/);
        assert.match(root.textContent!, /per kWh\)/);
        assert.equal(root.querySelector('.btn.danger'), null);

    });

    it('says so where there is no such session', async () => {

        const root = await open(sessionPage, '/charging/sessions/NOSUCHONE', [ 'charging:read' ],
                                smartphone, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /no charging session 'NOSUCHONE'/);

    });

});
