import { create } from 'zustand';

/** Client-only UI state. Anything that comes from the server belongs in TanStack Query instead. */
export interface Toast {
  id: string;
  title: string;
  body: string;
  link?: string | undefined;
}

interface UiState {
  copilotOpen: boolean;
  copilotClaimId: string | null;
  toasts: Toast[];
  openCopilot: (claimId?: string) => void;
  closeCopilot: () => void;
  pushToast: (toast: Toast) => void;
  dismissToast: (id: string) => void;
}

export const useUiStore = create<UiState>()((set) => ({
  copilotOpen: false,
  copilotClaimId: null,
  toasts: [],
  openCopilot: (claimId) => set({ copilotOpen: true, copilotClaimId: claimId ?? null }),
  closeCopilot: () => set({ copilotOpen: false }),
  pushToast: (toast) => set((s) => ({ toasts: [...s.toasts.filter((t) => t.id !== toast.id), toast].slice(-4) })),
  dismissToast: (id) => set((s) => ({ toasts: s.toasts.filter((t) => t.id !== id) })),
}));
