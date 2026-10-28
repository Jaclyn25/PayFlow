import React, { useState } from 'react';
import { useNavigate, useLocation, useSearchParams, Link } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import * as z from 'zod';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useAuth } from '@/context/AuthContext';
import { Lock, Mail, AlertCircle, ArrowRight, ShieldCheck } from 'lucide-react';

const loginSchema = z.object({
  email: z
    .string()
    .min(1, 'Email address is required')
    .email('Please enter a valid email address'),
  password: z
    .string()
    .min(6, 'Password must be at least 6 characters long'),
});

type LoginFormValues = z.infer<typeof loginSchema>;

export const Login: React.FC = () => {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const { login } = useAuth();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    setValue,
    formState: { errors, isSubmitting },
  } = useForm<LoginFormValues>({
    resolver: zodResolver(loginSchema),
    defaultValues: {
      email: '',
      password: '',
    },
  });

  const onSubmit = async (values: LoginFormValues) => {
    setServerError(null);
    try {
      await login(values);
      const rawReturnUrl = searchParams.get('returnUrl');
      const returnUrl = rawReturnUrl
        ? decodeURIComponent(rawReturnUrl)
        : (location.state as any)?.from?.pathname || '/';

      navigate(returnUrl, { replace: true });
    } catch (err: any) {
      const message =
        err?.response?.data?.message ||
        err?.response?.data?.title ||
        err?.message ||
        'Authentication failed. Please check your credentials.';
      setServerError(message);
    }
  };

  const handleFillDemo = (demoEmail: string) => {
    setValue('email', demoEmail, { shouldValidate: true });
    setValue('password', 'Password123!', { shouldValidate: true });
    setServerError(null);
  };

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 flex flex-col justify-center items-center px-4 py-12">
      <div className="w-full max-w-md space-y-6">
        <div className="text-center">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-primary-600 text-white font-bold text-2xl shadow-lg shadow-primary-500/30 mb-3">
            P
          </div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
            Sign in to PayFlow
          </h1>
          <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">
            Enterprise Financial Engine & Real-Time Settlement
          </p>
        </div>

        {serverError && (
          <div className="p-3.5 rounded-lg bg-rose-50 dark:bg-rose-950/40 border border-rose-200 dark:border-rose-800 flex items-center space-x-2 text-rose-800 dark:text-rose-300 text-sm">
            <AlertCircle className="w-4 h-4 flex-shrink-0" />
            <span>{serverError}</span>
          </div>
        )}

        <Card>
          <CardHeader>
            <CardTitle>Welcome Back</CardTitle>
            <CardDescription>Enter your account credentials to access your dashboard</CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
              <Input
                label="Email Address"
                type="email"
                placeholder="name@company.com"
                {...register('email')}
                error={errors.email?.message}
                leftIcon={<Mail className="w-4 h-4 text-slate-400" />}
              />

              <Input
                label="Password"
                type="password"
                placeholder="••••••••••••"
                {...register('password')}
                error={errors.password?.message}
                leftIcon={<Lock className="w-4 h-4 text-slate-400" />}
              />

              <Button
                type="submit"
                className="w-full mt-2"
                isLoading={isSubmitting}
                disabled={isSubmitting}
              >
                Sign In
                <ArrowRight className="w-4 h-4 ml-2" />
              </Button>
            </form>

            <div className="mt-6 pt-4 border-t border-slate-200 dark:border-slate-800">
              <div className="flex items-center justify-between mb-2">
                <span className="text-xs font-semibold text-slate-400 uppercase tracking-wider">
                  Quick Demo Accounts
                </span>
                <span className="inline-flex items-center text-xs text-emerald-600 dark:text-emerald-400">
                  <ShieldCheck className="w-3.5 h-3.5 mr-1" />
                  Auto-populated
                </span>
              </div>
              <div className="grid grid-cols-2 gap-2">
                <Button
                  variant="outline"
                  size="sm"
                  type="button"
                  onClick={() => handleFillDemo('admin@payflow.local')}
                >
                  Demo Admin
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  type="button"
                  onClick={() => handleFillDemo('user@payflow.local')}
                >
                  Demo User
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>

        <p className="text-center text-sm text-slate-500">
          Don't have an account?{' '}
          <Link to="/register" className="font-semibold text-primary-600 hover:text-primary-500">
            Create an account
          </Link>
        </p>
      </div>
    </div>
  );
};
