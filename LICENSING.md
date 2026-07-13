# Taskdeck Licensing Commitment

Last Updated: 2026-07-13

Taskdeck's open-source core is licensed under the [MIT License](LICENSE). This
document records the project's long-term licensing boundary; it does not
replace or narrow the rights already granted by that license.

## MIT Forever

Everything published in this repository under the MIT License remains MIT
licensed permanently. Taskdeck will not retroactively relicense that code,
withdraw the MIT grant, or put an existing MIT-licensed capability behind a
different license. Released versions remain usable, modifiable, and
redistributable under the MIT terms that accompanied them.

Contributions to the MIT-licensed core are accepted under the same MIT terms
(inbound equals outbound). Taskdeck uses the Developer Certificate of Origin
rather than a contributor licence agreement; see [CONTRIBUTING.md](CONTRIBUTING.md).

## Additive Commercial Modules

Future commercial capabilities, if any, will be additive and separately
licensed. They may live under the reserved [`ee/`](ee/) path or in a separate
repository, and each such module will carry its own explicit licence terms.
Commercial development will not be implemented by relicensing or re-gating an
existing MIT-licensed feature.

The `ee/` directory currently contains only a licence placeholder. It contains
no commercial product code and is outside the root MIT grant.

## Free Boundary

The following capabilities are part of Taskdeck's permanent free boundary and
will never be gated behind a commercial licence:

- the core capture -> proposal -> review -> apply loop;
- data export and portability;
- bring-your-own API key and local-LLM use; and
- single-user self-hosting.

Capabilities already shipped free, including MFA, OIDC, and board sharing,
also remain free. A future paid offering may add separately licensed modules or
managed services, but it will not subtract from this boundary.

## Name and Logo

The MIT licence covers the software and documentation identified by the root
licence. It does not grant permission to use the Taskdeck name, logo, or other
brand identifiers as trademarks, or to imply endorsement by the Taskdeck
project. Descriptive references to the project remain permitted as applicable
law allows.

No trademark registration or availability claim is made in this document.
