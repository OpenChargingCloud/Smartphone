/**
 * One charging session, drawn in a document of happy-dom against a stand-in
 * smartphone: what it charged and where, Stop while it starts or charges and
 * not after, one the e-mobility provider reports with the operator's figures
 * and without what OCPI does not tell, one at a station of a roaming network
 * with its meter values and its price checked, and a session that is not
 * there said so.
 */

import { asked, leave, open, refused, until, type Asked } from '../../test/smartphone.ts';
import { endedAtLimitSession, liveLinkSession, reportedSession, session, sessionCheck, stopLinkSession, wrongEndSession, wrongStartSession, wrongValueSession } from '../../test/charging.ts';
import type { MeterValueCheck } from '../api/client.ts';

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

    if (method === 'POST' && path === `/charging/sessions/${current.id}/dispute` && current.liveLink !== undefined) {
        current = { ...current, liveLink: { ...current.liveLink, dispute: { ...current.liveLink.dispute!, requestedBy: 'root', accepted: true, answer: '202 Accepted: The dispute is taken.' } } };
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

    it('shows the live link of a session its operator\'s web payment page started, what came over it, and no Stop', async () => {

        current = liveLinkSession();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-live-link') !== null);

        const link = root.querySelector<HTMLElement>('.session-live-link')!;

        assert.match(link.textContent!, /https:\/\/pay\.gef\.example\/liveLinks\/S1/);
        assert.match(link.textContent!, /Followed over\s*https/);
        assert.match(link.textContent!, /Signed documents\s*2/);
        assert.match(link.textContent!, /https:\/\/pay\.gef\.example\/sessions\/S1\/live/);
        assert.equal(link.querySelectorAll('ul.live-readings li').length, 2);
        assert.equal(link.querySelectorAll('ul.live-readings li.verified').length, 2, 'a reading not said to be the meter\'s');
        assert.ok(link.querySelector('.live-start.verified'), 'its start value is not said to be the meter\'s');
        assert.match(link.querySelector('.live-start')!.textContent!, /the meter's\s*Start value/);
        assert.match(link.querySelector('.live-start')!.textContent!, /meter DC-2026-001, key 95ceb48c[^]*identified by\s*web-payment-S1/);
        assert.match(link.textContent!, /Payment reference\s*web-payment-S1: every signed value is to name it/);
        assert.equal(link.querySelector('.live-wrong'), null, 'a wrong value where none is');
        assert.match(root.textContent!, /Started at the web payment page of its operator, and stopped there/);
        assert.match(root.textContent!, /1 signed value after it is the meter's as well, and fits the values before/);
        assert.equal(root.querySelector('.btn.danger'), null, 'a Stop for a session stopped at the web payment page');

    });

    it('says the phone stopped a session whose signed value after the start is a wrong one, and why', async () => {

        current = wrongValueSession();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.live-wrong') !== null);

        const wrong = root.querySelector<HTMLElement>('.live-wrong')!;

        assert.match(wrong.textContent!, /a wrong meter value\s*After the start/);
        assert.match(wrong.textContent!, /990[.,]000 kWh/);
        assert.match(wrong.textContent!, /The meter counts backwards/);
        assert.match(wrong.querySelector('.error-box')!.textContent!, /a signed meter value after its start is a wrong one/);
        assert.match(wrong.querySelector('.error-box')!.textContent!, /stop it there/);
        assert.ok(root.querySelector('.live-start.verified'), 'its start value held');
        assert.equal(root.querySelectorAll('ul.live-readings li.refused').length, 1);
        assert.match(root.querySelector('.session-status')!.textContent!, /stopped/);
        assert.equal(root.querySelector('.btn.danger'), null, 'a Stop for a session the phone stopped');

    });

    it('says the phone stopped a session whose signed start value is a wrong one, and where to stop the station', async () => {

        current = wrongStartSession();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.live-start') !== null);

        const start = root.querySelector<HTMLElement>('.live-start')!;

        assert.ok(start.classList.contains('refused'), start.className);
        assert.match(start.textContent!, /a wrong start value/);
        assert.match(start.textContent!, /InvalidSignature/);
        assert.match(start.querySelector('.error-box')!.textContent!, /This phone stopped the session/);
        assert.match(start.querySelector('.error-box')!.textContent!, /stop it there/);
        assert.match(root.querySelector('.session-status')!.textContent!, /stopped/);
        assert.equal(root.querySelector('.btn.danger'), null, 'a Stop for a session the phone stopped');

    });

    it('stops a session through the stop link its operator\'s web payment page gave, and shows its limit', async () => {

        current = stopLinkSession();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.live-limits') !== null);

        assert.match(root.querySelector('.session-live-link')!.textContent!, /Stop link\s*at pay\.gef\.example/);
        assert.doesNotMatch(root.textContent!, /\/stop\b/, 'the stop link itself is shown');
        assert.match(root.querySelector('.live-limits')!.textContent!, /Limit 20 kWh\s*12[.,]5 kWh/);
        assert.ok(root.querySelector('.live-limits li.not-reached'));
        assert.match(root.querySelector('.btn.danger')!.textContent!, /Stop charging/);

        root.querySelector<HTMLButtonElement>('.btn.danger')!.click();

        await until(() => asked.some(one => one.method === 'POST' && one.path === `/charging/sessions/${current.id}/stop`),
                    'Stop did not ask the smartphone');

    });

    it('says a session ended at the limit the driver chose, and offers no Stop then', async () => {

        current = endedAtLimitSession();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.live-ending') !== null);

        const ending = root.querySelector<HTMLElement>('.live-ending')!;

        assert.ok(ending.classList.contains('limit'), ending.className);
        assert.match(ending.textContent!, /at its limit\s*It ended at its limit of 20 kWh: 20 kWh/);
        assert.ok(root.querySelector('.live-limits li.reached'));
        assert.match(root.querySelector('.session-reported')!.textContent!, /It is over: It ended at its limit of 20 kWh/);
        assert.equal(root.querySelector('.btn.danger'), null, 'a Stop for a session that ended');

    });

    it('says the phone stopped the station of a session with a wrong value through its stop link', async () => {

        const wrong = wrongValueSession();

        current = { ...wrong, liveLink: { ...wrong.liveLink!, stopLink: 'pay.gef.example',
                                          stop: { requestedAt: '2026-10-10T08:10:06Z', requestedBy: 'this phone', accepted: true, answer: '200 OK: The station stops.' } } };

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.live-wrong') !== null);

        const box = root.querySelector<HTMLElement>('.live-wrong .error-box')!;

        assert.match(box.textContent!, /This phone stopped the station through the stop link of the web payment page/);
        assert.doesNotMatch(box.textContent!, /stop it there/);
        assert.match(root.querySelector('.session-live-link')!.textContent!, /Stop asked[^]*by this phone:\s*taken - 200 OK/);
        assert.equal(root.querySelector('.btn.danger'), null, 'a Stop for a session the phone stopped');

    });

    it('says a signed end value is a wrong one, and disputes the session again where its web payment page did not take the dispute', async () => {

        current = wrongEndSession();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.live-dispute') !== null);

        const end      = root.querySelector<HTMLElement>('.live-end')!;
        const dispute  = root.querySelector<HTMLElement>('.live-dispute')!;

        assert.ok(end.classList.contains('refused'), end.className);
        assert.match(end.textContent!, /a wrong end value[^]*End value[^]*\(E\)[^]*not signed by the key/);
        assert.match(root.querySelector('.live-ending')!.textContent!, /a wrong end value\s*Its signed end meter value is a wrong one/);
        assert.match(dispute.textContent!, /did not take the dispute this phone sent: 409 Conflict: Nothing is disputed here\. Dispute it there\./);
        assert.match(root.querySelector('.session-live-link')!.textContent!, /Dispute link\s*at pay\.gef\.example/);
        assert.match(root.querySelector('.session-live-link')!.textContent!, /Dispute sent[^]*by this phone:\s*not taken - 409/);
        assert.match(root.querySelector('.session-reported')!.textContent!, /It is over: Its signed end meter value is a wrong one[^]*Dispute it there\./);
        assert.equal(root.querySelector('.btn.danger'), null, 'a Stop for a session that ended');

        root.querySelector<HTMLButtonElement>('.live-dispute .btn.dispute')!.click();

        await until(() => asked.some(one => one.method === 'POST' && one.path === `/charging/sessions/${current.id}/dispute`),
                    'Dispute again did not ask the smartphone');
        await until(() => root.querySelector('.live-dispute.taken') !== null, 'the dispute taken is not shown');

        assert.match(root.querySelector('.live-dispute')!.textContent!, /This phone disputed it through the dispute link of the web payment page/);
        assert.equal(root.querySelector('.live-dispute .btn.dispute'), null, 'Dispute again for a dispute the page took');

    });

    it('says the end value of a session that ended at its limit is the meter\'s, and disputes nothing', async () => {

        current = endedAtLimitSession();

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.live-end') !== null);

        assert.ok(root.querySelector('.live-end.verified'));
        assert.equal(root.querySelector('.live-dispute'), null, 'a session whose values hold is disputed');
        assert.match(root.querySelector('.session-live-link')!.textContent!, /Dispute link\s*none given: it is disputed at the page/);

    });

    it('says the phone stopped a session whose signed value names another payment, and where none was given that nothing binds its values', async () => {

        const followed = wrongValueSession();
        const link     = followed.liveLink!;
        const error    = 'It is of another session: it names "web-payment-S2" as the identification of the charging ("ID"), and the web payment page took the payment "web-payment-S1" for this session.';

        current = { ...followed, stoppedBecause: `A signed meter value after its start is a wrong one: ${error}`,
                    liveLink: { ...link, disputeBecause: `A signed meter value after its start is a wrong one: ${error}`,
                                wrong: { ...link.wrong!, error, identification: 'web-payment-S2' } } };

        let root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read' ],
                              smartphone, root => root.querySelector('.live-wrong') !== null);

        assert.match(root.querySelector('.live-wrong')!.textContent!, /identified by\s*web-payment-S2[^]*It is of another session/);

        current = liveLinkSession({ liveLink: { ...liveLinkSession().liveLink!, paymentReference: null } });

        root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read' ],
                          smartphone, root => root.querySelector('.session-live-link') !== null);

        assert.match(root.querySelector('.session-live-link')!.textContent!, /Payment reference\s*none given: that its values are of this payment is not proven/);

    });

    it('says no signed start value came over a live link yet, and one that could not be checked', async () => {

        const link = liveLinkSession().liveLink!;

        current = liveLinkSession({ liveLink: { ...link, start: null } });

        let root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read' ],
                              smartphone, root => root.querySelector('.live-start') !== null);

        assert.ok(root.querySelector('.live-start.pending'));
        assert.match(root.querySelector('.live-start')!.textContent!, /No signed start value came over it yet/);

        current = liveLinkSession({ liveLink: { ...link, start: { ...link.start!, holds: null, keyId: null, meterId: null, result: null,
                                                                  error: 'No key of the meter DC-2026-001 of DE*GEF*E2*1 is attested for when it was measured.' } } });

        root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read' ],
                          smartphone, root => root.querySelector('.live-start') !== null);

        assert.ok(root.querySelector('.live-start.unchecked'));
        assert.match(root.querySelector('.live-start')!.textContent!, /not checked\s*Start value/);
        assert.match(root.querySelector('.live-start')!.textContent!, /is attested for when it was measured/);
        assert.equal(root.querySelector('.live-start .error-box'), null, 'a session stopped that was not');

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

    it('says the meter signed the values of the charge detail record, and the price the operator stated for its start', async () => {

        current = reportedSession({ status: 'finished', energyKWh: 12.5, endedAt: '2026-10-10T09:30:00Z',
                                    cost: { amount: 8.13, pricePerKWh: 0.65, currency: 'EUR', reported: true },
                                    provider: { state: 'ended', receivedAt: '2026-10-10T09:31:00Z', cdr: true, result: 'ACCEPTED', sessionStatus: 'COMPLETED' },
                                    check: sessionCheck() });

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read' ],
                                smartphone, root => root.querySelector('.session-check') !== null);

        const check = root.querySelector('.session-check')!.textContent!;

        assert.match(check, /signed by the meter/);
        assert.equal(root.querySelectorAll('.meter-value.verified').length, 2);
        assert.match(check, /12[.,]500 kWh in all/);
        assert.match(check, /The record says\s*12[.,]500 kWh\s*the same/);
        assert.match(check, /95ceb48cc35ae3335c7f06fa0a8b8aff446da032057708eec66a7edc371591f6/);
        assert.match(check, /identified by this start/);
        assert.match(check, /not a key the record names itself/);
        assert.match(root.querySelector('.session-price')!.textContent!, /DE\*GEF, for EVSE DE\*GEF\*E2\*1/);
        assert.match(root.querySelector('.session-price')!.textContent!, /The operator signed no assignment of it to this session/);
        assert.doesNotMatch(check, /counted with/);

    });

    it("says which meter value is not the meter's, and why, and where the phone counted with another price", async () => {

        const checked = sessionCheck();

        const forged: MeterValueCheck = { ...checked.meterValues!.values[1]!, verified: false, result: 'InvalidSignature',
                                          error: 'It is not signed by the key 95ceb48c… of the meter DC-2026-001: InvalidSignature.' };

        current = reportedSession({ status: 'finished', endedAt: '2026-10-10T09:30:00Z',
                                    provider: { state: 'ended', receivedAt: '2026-10-10T09:31:00Z', cdr: true },
                                    check: { ...checked,
                                             price:        { ...checked.price!, matchesSession: false },
                                             meterValues:  { ...checked.meterValues!, verified: false, values: [ checked.meterValues!.values[0]!, forged ],
                                                             endKWh: 1000, energyKWh: null, energyMatches: null, expectedCost: null, costMatches: null } } });

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read' ],
                                smartphone, root => root.querySelector('.session-check') !== null);

        const check = root.querySelector('.session-check')!.textContent!;

        assert.match(check, /not all signed by the meter/);
        assert.equal(root.querySelectorAll('.meter-value.not-verified').length, 1);
        assert.match(root.querySelector('.meter-value.not-verified')!.textContent!, /not the meter's/);
        assert.match(root.querySelector('.meter-value.not-verified')!.textContent!, /InvalidSignature/);
        assert.match(check, /no begin and end the meter signed/);
        assert.match(check, /this phone counted with/);

    });

    it('says nothing of it is checked where the identity keys that attested its station are no longer trusted', async () => {

        current = reportedSession({ check: { checkedAt: '2026-10-10T09:31:00Z',
                                             error: "Nothing of it is checked: none of the identity keys of the trust anchor of 'QA' is trusted here." } });

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read' ],
                                smartphone, root => root.querySelector('.session-check') !== null);

        assert.match(root.querySelector('.check-error')!.textContent!, /Nothing of it is checked/);
        assert.equal(root.querySelector('.meter-values-pending'), null, 'a promise of a check that will not come');
        assert.equal(root.querySelector('.session-price'), null);

    });

    it('says the meter values are checked once the charge detail record came', async () => {

        const checked = sessionCheck();

        delete checked.meterValues;

        current = reportedSession({ check: checked });

        const root = await open(sessionPage, `/charging/sessions/${current.id}`, [ 'charging:read', 'charging:run' ],
                                smartphone, root => root.querySelector('.session-check') !== null);

        assert.ok(root.querySelector('.meter-values-pending'), 'no word of the meter values to come');
        assert.match(root.querySelector('.session-price')!.textContent!, /0[.,]65/);

    });

    it('says so where there is no such session', async () => {

        const root = await open(sessionPage, '/charging/sessions/NOSUCHONE', [ 'charging:read' ],
                                smartphone, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /no charging session 'NOSUCHONE'/);

    });

});
