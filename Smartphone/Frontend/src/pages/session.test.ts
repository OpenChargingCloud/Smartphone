/**
 * One charging session, drawn in a document of happy-dom against a stand-in
 * smartphone: what it charged and where, Stop while it charges and not after,
 * and a session that is not there said so.
 */

import { asked, leave, open, refused, until, type Asked } from '../../test/smartphone.ts';
import { session } from '../../test/charging.ts';

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

    });

    it('says so where there is no such session', async () => {

        const root = await open(sessionPage, '/charging/sessions/NOSUCHONE', [ 'charging:read' ],
                                smartphone, root => root.querySelector('.error-box') !== null);

        assert.match(root.querySelector('.error-box')!.textContent!, /no charging session 'NOSUCHONE'/);

    });

});
