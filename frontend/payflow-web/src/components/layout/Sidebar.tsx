import { NavLink } from 'react-router-dom';
import {
  LayoutDashboard,
  ArrowLeftRight,
  History,
  Settings,
  ShieldCheck,
  Webhook,
  HelpCircle,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import { useAuth } from '@/context/AuthContext';

export interface SidebarProps {
  isOpen: boolean;
  onClose?: () => void;
}

export function Sidebar({ isOpen, onClose }: SidebarProps) {
  const { user } = useAuth();

  const navigation = [
    { name: 'Dashboard', href: '/', icon: LayoutDashboard },
    { name: 'Transfers', href: '/transfers', icon: ArrowLeftRight },
    { name: 'History', href: '/history', icon: History },
    { name: 'Settings', href: '/settings', icon: Settings },
  ];

  if (user && (user.role === 'Admin' || user.role === 'SystemAdmin')) {
    navigation.push(
      { name: 'Admin Console', href: '/admin', icon: ShieldCheck },
      { name: 'Webhooks', href: '/webhooks', icon: Webhook }
    );
  }

  return (
    <>
      {/* Mobile backdrop */}
      {isOpen && (
        <div
          className="fixed inset-0 z-40 bg-background/80 backdrop-blur-sm md:hidden"
          onClick={onClose}
        />
      )}

      {/* Sidebar container */}
      <aside
        className={cn(
          'fixed inset-y-0 left-0 z-40 mt-16 flex w-64 flex-col border-r bg-card transition-transform duration-200 ease-in-out md:static md:mt-0 md:translate-x-0',
          isOpen ? 'translate-x-0' : '-translate-x-full'
        )}
      >
        <div className="flex flex-1 flex-col justify-between overflow-y-auto px-4 py-6">
          <nav className="space-y-1.5">
            <div className="px-3 pb-2 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
              Menu
            </div>
            {navigation.map((item) => {
              const Icon = item.icon;
              return (
                <NavLink
                  key={item.name}
                  to={item.href}
                  onClick={onClose}
                  className={({ isActive }) =>
                    cn(
                      'group flex items-center rounded-lg px-3 py-2.5 text-sm font-medium transition-colors',
                      isActive
                        ? 'bg-primary text-primary-foreground shadow-sm'
                        : 'text-muted-foreground hover:bg-accent hover:text-foreground'
                    )
                  }
                >
                  <Icon className="mr-3 h-5 w-5 shrink-0" />
                  <span>{item.name}</span>
                </NavLink>
              );
            })}
          </nav>

          <div className="pt-6 border-t space-y-3">
            <div className="rounded-xl border bg-muted/50 p-4">
              <div className="flex items-center space-x-2 text-xs font-medium text-foreground">
                <HelpCircle className="h-4 w-4 text-primary" />
                <span>Need Assistance?</span>
              </div>
              <p className="mt-1.5 text-xs text-muted-foreground leading-relaxed">
                Contact PayFlow 24/7 financial support or consult API documentation.
              </p>
            </div>
          </div>
        </div>
      </aside>
    </>
  );
}
