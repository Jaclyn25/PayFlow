import React, { useState } from 'react';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { Badge } from '@/components/ui/badge';
import { useAuth } from '@/context/AuthContext';
import { useTheme } from '@/context/ThemeContext';
import { Key, Moon, Sun, Smartphone, UserCheck, Bell } from 'lucide-react';

export const Settings: React.FC = () => {
  const { user } = useAuth();
  const { theme, toggleTheme } = useTheme();
  const [twoFactorEnabled, setTwoFactorEnabled] = useState(user?.twoFactorEnabled ?? true);
  const [webhookUrl, setWebhookUrl] = useState('https://merchant.example.com/api/payflow-hooks');
  const [savedAlert, setSavedAlert] = useState(false);

  const handleSave = (e: React.FormEvent) => {
    e.preventDefault();
    setSavedAlert(true);
    setTimeout(() => setSavedAlert(false), 3000);
  };

  return (
    <div className="space-y-6 max-w-4xl mx-auto">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
          Account Settings
        </h1>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Manage your personal profile, security credentials, and organization preferences.
        </p>
      </div>

      {savedAlert && (
        <div className="p-4 rounded-xl bg-emerald-50 dark:bg-emerald-950/40 border border-emerald-200 dark:border-emerald-800 text-sm font-medium text-emerald-800 dark:text-emerald-300">
          Settings successfully updated.
        </div>
      )}

      {/* Profile Overview */}
      <Card>
        <CardHeader>
          <div className="flex items-center justify-between">
            <div>
              <CardTitle>Profile Details</CardTitle>
              <CardDescription>Your registered identity and account tier</CardDescription>
            </div>
            <Badge variant="success">
              <UserCheck className="w-3.5 h-3.5 mr-1" />
              KYC Level 2 Approved
            </Badge>
          </div>
        </CardHeader>
        <CardContent>
          <form onSubmit={handleSave} className="space-y-4">
            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <Input label="Full Name" defaultValue={user ? `${user.firstName} ${user.lastName}` : 'Administrator'} />
              <Input label="Email Address" defaultValue={user?.email || 'admin@payflow.local'} disabled />
            </div>

            <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <Input label="Role" defaultValue={user?.role || 'Admin'} disabled />
              <Input label="User Identifier" defaultValue={user?.id || 'usr_master_001'} disabled />
            </div>

            <div className="flex justify-end pt-2">
              <Button type="submit">Save Changes</Button>
            </div>
          </form>
        </CardContent>
      </Card>

      {/* Security & Authentication */}
      <Card>
        <CardHeader>
          <CardTitle>Security & Multi-Factor Authentication</CardTitle>
          <CardDescription>
            Protect your wallet operations with two-factor authorization and cryptographic device keys.
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <div className="flex items-center justify-between p-4 rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-900/40">
            <div className="flex items-center space-x-3">
              <div className="w-10 h-10 rounded-full bg-primary-100 dark:bg-primary-950/60 flex items-center justify-center text-primary-600 dark:text-primary-400">
                <Smartphone className="w-5 h-5" />
              </div>
              <div>
                <p className="text-sm font-semibold text-slate-900 dark:text-white">
                  Authenticator App (TOTP)
                </p>
                <p className="text-xs text-slate-500">
                  Google Authenticator, 1Password, or Authy time-based verification codes.
                </p>
              </div>
            </div>
            <Button
              variant={twoFactorEnabled ? 'secondary' : 'default'}
              size="sm"
              onClick={() => setTwoFactorEnabled(!twoFactorEnabled)}
            >
              {twoFactorEnabled ? 'Disable 2FA' : 'Enable 2FA'}
            </Button>
          </div>

          <div className="flex items-center justify-between p-4 rounded-lg border border-slate-200 dark:border-slate-800 bg-slate-50 dark:bg-slate-900/40">
            <div className="flex items-center space-x-3">
              <div className="w-10 h-10 rounded-full bg-slate-100 dark:bg-slate-800 flex items-center justify-center text-slate-600 dark:text-slate-300">
                <Key className="w-5 h-5" />
              </div>
              <div>
                <p className="text-sm font-semibold text-slate-900 dark:text-white">
                  Account Password
                </p>
                <p className="text-xs text-slate-500">
                  Last changed 14 days ago. Minimum 12 characters required.
                </p>
              </div>
            </div>
            <Button variant="outline" size="sm">
              Change Password
            </Button>
          </div>
        </CardContent>
      </Card>

      {/* Developer Webhooks */}
      <Card>
        <CardHeader>
          <CardTitle>Developer & Webhook Subscriptions</CardTitle>
          <CardDescription>
            Live event deliveries configured for Outbox & Webhook engine
          </CardDescription>
        </CardHeader>
        <CardContent className="space-y-4">
          <Input
            label="Default Webhook Delivery URL"
            value={webhookUrl}
            onChange={(e) => setWebhookUrl(e.target.value)}
            leftIcon={<Bell className="w-4 h-4 text-slate-400" />}
          />
          <div className="flex items-center justify-between text-xs text-slate-500">
            <span className="font-mono">Signing Secret: whsec_7f9a2e8c... (Rotated 3 days ago)</span>
            <Button variant="outline" size="sm">Send Test Ping</Button>
          </div>
        </CardContent>
      </Card>

      {/* Appearance */}
      <Card>
        <CardHeader>
          <CardTitle>Interface Appearance</CardTitle>
          <CardDescription>Toggle between dark and light themes</CardDescription>
        </CardHeader>
        <CardContent>
          <div className="flex items-center justify-between">
            <div className="flex items-center space-x-3">
              {theme === 'dark' ? (
                <Moon className="w-5 h-5 text-primary-400" />
              ) : (
                <Sun className="w-5 h-5 text-amber-500" />
              )}
              <span className="text-sm font-medium text-slate-900 dark:text-white capitalize">
                {theme} Mode Active
              </span>
            </div>
            <Button variant="secondary" size="sm" onClick={toggleTheme}>
              Switch to {theme === 'dark' ? 'Light' : 'Dark'} Mode
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>
  );
};
