import { nodeAPI,
         type Certificate        as NodeCertificate,
         type CertificateImport  as NodeCertificateImport,
         type CertificateStore   as NodeCertificateStore,
         type NodeConfiguration,
         type NodeMe,
         type NodeResource,
         type NodeStatus,
         type Operation }  from '@node/api/client';


// What every node answers - the log, name resolution, the time, the store,
// who is signed in - and how it is asked are WWCP_Node's, and every page here
// reads them from this module. What follows is what a smartphone says of its
// own: its configuration's own section and the kinds its store keeps. Its
// account at an e-mobility provider, its charging tickets and the session it
// watches will be added here.
export * from '@node/api/client';


/** What a role may be allowed to touch on this smartphone: what every node has, and nothing more yet. */
export type Resource = NodeResource;

/** What somebody signed in to this smartphone may do: an operation on a resource, written "certificates:edit". */
export type Permission = `${Resource}:${Operation}`;

/** Who is signed in to the web interface. */
export type Me = NodeMe<Resource>;

/** How the smartphone is doing right now: what every node says, and nothing more yet. */
export type Status = NodeStatus;

/**
 * What the smartphone is made of: every node's sections, and its own. Only
 * the shape the Configuration page relies on is named; the fields of each
 * section are rendered from whatever the smartphone sends.
 */
export interface Configuration extends NodeConfiguration {
    smartphone:  Record<string, unknown>;
}

/**
 * What a certificate in the store is for: the roots the smartphone believes,
 * the server certificates it holds a server to, and who it is as a client.
 * No root of clients - nobody connects to a smartphone with a certificate.
 */
export type CertificateKind = 'tlsRoot' | 'tlsServer' | 'tlsIdentity';

/** One certificate in the store. */
export type Certificate = NodeCertificate<CertificateKind>;

/** What an import into the store sends. */
export type CertificateImport = NodeCertificateImport<CertificateKind>;

/** The store, grouped the way it is shown. */
export type CertificateStore = NodeCertificateStore<CertificateKind>;


/** How the smartphone is asked: the routes every node has, typed with the smartphone's own of them. */
export const api = {

    ...nodeAPI<{ me: Me; status: Status; configuration: Configuration; kind: CertificateKind; store: CertificateStore }>()

};
