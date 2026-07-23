import type { RagStatus } from '../../api/client';
import { strings } from '../../strings';

export interface RagChipProps {
  status: RagStatus;
}

const RAG_VARIANT: Record<RagStatus, 'green' | 'amber' | 'red'> = {
  OnTrack: 'green',
  AtRisk: 'amber',
  OffTrack: 'red',
};

/**
 * RAG-status chip for an initiative (DESIGN.md A2 RAG; mockup `.rag`). The status LABEL is always
 * printed ("On Track" / "At Risk" / "Off Track") — the RAG colour is reinforcement only, never the sole
 * signal (A2/A5 colour-independence).
 */
export function RagChip({ status }: RagChipProps): JSX.Element {
  const variant = RAG_VARIANT[status] ?? 'green';
  return <span className={`rag-chip rag-chip-${variant}`}>{strings.register.ragLabels[status]}</span>;
}
