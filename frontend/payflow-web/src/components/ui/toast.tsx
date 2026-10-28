import { useState, useEffect } from 'react';
import { CheckCircle2, AlertCircle, Info, X } from 'lucide-react';
import { cn } from '@/lib/utils';

export type ToastType = 'success' | 'error' | 'info';

export interface ToastMessage {
  id: string;
  type: ToastType;
  title: string;
  message?: string;
  duration?: number;
}

let toastListener: ((toast: ToastMessage) => void) | null = null;

export const toast = {
  success: (title: string, message?: string) => {
    toastListener?.({ id: Math.random().toString(), type: 'success', title, message });
  },
  error: (title: string, message?: string) => {
    toastListener?.({ id: Math.random().toString(), type: 'error', title, message });
  },
  info: (title: string, message?: string) => {
    toastListener?.({ id: Math.random().toString(), type: 'info', title, message });
  },
};

export function ToastContainer() {
  const [toasts, setToasts] = useState<ToastMessage[]>([]);

  useEffect(() => {
    toastListener = (newToast) => {
      setToasts((prev) => [...prev, newToast]);
      setTimeout(() => {
        setToasts((prev) => prev.filter((t) => t.id !== newToast.id));
      }, newToast.duration || 4000);
    };

    return () => {
      toastListener = null;
    };
  }, []);

  const removeToast = (id: string) => {
    setToasts((prev) => prev.filter((t) => t.id !== id));
  };

  if (toasts.length === 0) return null;

  return (
    <div className="fixed bottom-4 right-4 z-50 flex flex-col space-y-2 max-w-sm w-full pointer-events-none">
      {toasts.map((t) => {
        const icons = {
          success: <CheckCircle2 className="h-5 w-5 text-emerald-500 shrink-0" />,
          error: <AlertCircle className="h-5 w-5 text-destructive shrink-0" />,
          info: <Info className="h-5 w-5 text-primary shrink-0" />,
        };

        return (
          <div
            key={t.id}
            className={cn(
              'pointer-events-auto flex items-start space-x-3 rounded-lg border bg-card p-4 shadow-lg transition-all animate-in slide-in-from-bottom-5',
              t.type === 'error' && 'border-destructive/30',
              t.type === 'success' && 'border-emerald-500/30'
            )}
          >
            {icons[t.type]}
            <div className="flex-1 space-y-1">
              <h4 className="text-sm font-semibold text-foreground leading-tight">{t.title}</h4>
              {t.message && <p className="text-xs text-muted-foreground">{t.message}</p>}
            </div>
            <button
              onClick={() => removeToast(t.id)}
              className="text-muted-foreground hover:text-foreground transition-colors p-0.5"
            >
              <X className="h-4 w-4" />
            </button>
          </div>
        );
      })}
    </div>
  );
}
