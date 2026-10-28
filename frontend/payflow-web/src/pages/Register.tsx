import React, { useState } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import * as z from 'zod';
import { Card, CardHeader, CardTitle, CardDescription, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useAuth } from '@/context/AuthContext';
import { Lock, Mail, User, AlertCircle, ArrowRight } from 'lucide-react';

const registerSchema = z
  .object({
    firstName: z
      .string()
      .min(2, 'First name must be at least 2 characters'),
    lastName: z
      .string()
      .min(2, 'Last name must be at least 2 characters'),
    email: z
      .string()
      .min(1, 'Email address is required')
      .email('Please enter a valid email address'),
    currency: z.string(),
    password: z
      .string()
      .min(8, 'Password must be at least 8 characters')
      .regex(/[A-Z]/, 'Password must contain at least one uppercase letter')
      .regex(/[0-9]/, 'Password must contain at least one number'),
    confirmPassword: z
      .string()
      .min(1, 'Please confirm your password'),
    agreeTerms: z
      .boolean()
      .refine((val) => val === true, 'You must agree to the Terms of Service'),
  })
  .refine((data) => data.password === data.confirmPassword, {
    message: 'Passwords do not match',
    path: ['confirmPassword'],
  });

type RegisterFormValues = z.infer<typeof registerSchema>;

export const Register: React.FC = () => {
  const navigate = useNavigate();
  const { register: registerAuth } = useAuth();
  const [serverError, setServerError] = useState<string | null>(null);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<RegisterFormValues>({
    resolver: zodResolver(registerSchema),
    defaultValues: {
      firstName: '',
      lastName: '',
      email: '',
      currency: 'USD',
      password: '',
      confirmPassword: '',
      agreeTerms: false,
    },
  });

  const onSubmit = async (values: RegisterFormValues) => {
    setServerError(null);
    try {
      await registerAuth({
        firstName: values.firstName,
        lastName: values.lastName,
        email: values.email,
        password: values.password,
        currency: values.currency,
      });
      navigate('/', { replace: true });
    } catch (err: any) {
      const message =
        err?.response?.data?.message ||
        err?.response?.data?.title ||
        err?.message ||
        'Registration failed. Please check your information and try again.';
      setServerError(message);
    }
  };

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 flex flex-col justify-center items-center px-4 py-12">
      <div className="w-full max-w-lg space-y-6">
        <div className="text-center">
          <div className="inline-flex items-center justify-center w-12 h-12 rounded-xl bg-primary-600 text-white font-bold text-2xl shadow-lg shadow-primary-500/30 mb-3">
            P
          </div>
          <h1 className="text-2xl font-bold tracking-tight text-slate-900 dark:text-white">
            Create PayFlow Account
          </h1>
          <p className="text-sm text-slate-500 dark:text-slate-400 mt-1">
            Enterprise Financial Infrastructure & Real-Time Wallets
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
            <CardTitle>Sign Up</CardTitle>
            <CardDescription>Enter your account details to initialize your financial wallet</CardDescription>
          </CardHeader>
          <CardContent>
            <form onSubmit={handleSubmit(onSubmit)} className="space-y-4" noValidate>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <Input
                  label="First Name"
                  placeholder="Jane"
                  {...register('firstName')}
                  error={errors.firstName?.message}
                  leftIcon={<User className="w-4 h-4 text-slate-400" />}
                />

                <Input
                  label="Last Name"
                  placeholder="Doe"
                  {...register('lastName')}
                  error={errors.lastName?.message}
                  leftIcon={<User className="w-4 h-4 text-slate-400" />}
                />
              </div>

              <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
                <div className="sm:col-span-2">
                  <Input
                    label="Corporate or Personal Email"
                    type="email"
                    placeholder="jane@example.com"
                    {...register('email')}
                    error={errors.email?.message}
                    leftIcon={<Mail className="w-4 h-4 text-slate-400" />}
                  />
                </div>
                <div>
                  <label className="block text-xs font-semibold uppercase tracking-wider text-slate-500 dark:text-slate-400 mb-1.5">
                    Currency
                  </label>
                  <select
                    {...register('currency')}
                    className="w-full h-10 px-3 rounded-lg border border-slate-300 dark:border-slate-700 bg-white dark:bg-slate-900 text-sm text-slate-900 dark:text-slate-100 focus:outline-none focus:ring-2 focus:ring-primary-500"
                  >
                    <option value="USD">USD ($)</option>
                    <option value="EUR">EUR (€)</option>
                    <option value="EGP">EGP (E£)</option>
                  </select>
                </div>
              </div>

              <Input
                label="Password"
                type="password"
                placeholder="Minimum 8 characters (1 uppercase, 1 digit)"
                {...register('password')}
                error={errors.password?.message}
                leftIcon={<Lock className="w-4 h-4 text-slate-400" />}
              />

              <Input
                label="Confirm Password"
                type="password"
                placeholder="Re-enter your password"
                {...register('confirmPassword')}
                error={errors.confirmPassword?.message}
                leftIcon={<Lock className="w-4 h-4 text-slate-400" />}
              />

              <div className="pt-2">
                <label className="flex items-start space-x-2.5 cursor-pointer">
                  <input
                    type="checkbox"
                    {...register('agreeTerms')}
                    className="mt-1 h-4 w-4 rounded border-slate-300 text-primary-600 focus:ring-primary-500"
                  />
                  <span className="text-xs text-slate-600 dark:text-slate-400">
                    I agree to the PayFlow{' '}
                    <span className="text-primary-600 underline">Terms of Service</span> and{' '}
                    <span className="text-primary-600 underline">Privacy Policy</span>.
                  </span>
                </label>
                {errors.agreeTerms?.message && (
                  <p className="text-xs text-rose-500 mt-1">{errors.agreeTerms.message}</p>
                )}
              </div>

              <Button
                type="submit"
                className="w-full mt-2"
                isLoading={isSubmitting}
                disabled={isSubmitting}
              >
                Create Account
                <ArrowRight className="w-4 h-4 ml-2" />
              </Button>
            </form>
          </CardContent>
        </Card>

        <p className="text-center text-sm text-slate-500">
          Already registered?{' '}
          <Link to="/login" className="font-semibold text-primary-600 hover:text-primary-500">
            Sign in
          </Link>
        </p>
      </div>
    </div>
  );
};
