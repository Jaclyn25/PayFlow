import { describe, it, expect } from 'vitest';
import * as z from 'zod';

const loginSchema = z.object({
  email: z
    .string()
    .min(1, 'Email address is required')
    .email('Please enter a valid email address'),
  password: z
    .string()
    .min(6, 'Password must be at least 6 characters long'),
});

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

describe('Auth Validation Schemas', () => {
  describe('loginSchema', () => {
    it('validates correct email and password', () => {
      const result = loginSchema.safeParse({
        email: 'user@payflow.local',
        password: 'Password123!',
      });
      expect(result.success).toBe(true);
    });

    it('rejects invalid email formats', () => {
      const result = loginSchema.safeParse({
        email: 'invalid-email-address',
        password: 'Password123!',
      });
      expect(result.success).toBe(false);
      if (!result.success) {
        expect(result.error.issues[0].message).toContain('valid email');
      }
    });

    it('rejects short passwords', () => {
      const result = loginSchema.safeParse({
        email: 'user@payflow.local',
        password: '123',
      });
      expect(result.success).toBe(false);
      if (!result.success) {
        expect(result.error.issues[0].message).toContain('at least 6 characters');
      }
    });
  });

  describe('registerSchema', () => {
    it('validates a complete, compliant registration payload', () => {
      const result = registerSchema.safeParse({
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'jane.doe@payflow.local',
        currency: 'USD',
        password: 'SecurePassword1',
        confirmPassword: 'SecurePassword1',
        agreeTerms: true,
      });
      expect(result.success).toBe(true);
    });

    it('fails when passwords do not match', () => {
      const result = registerSchema.safeParse({
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'jane.doe@payflow.local',
        currency: 'USD',
        password: 'SecurePassword1',
        confirmPassword: 'DifferentPassword2',
        agreeTerms: true,
      });
      expect(result.success).toBe(false);
      if (!result.success) {
        expect(result.error.issues[0].message).toBe('Passwords do not match');
      }
    });

    it('fails when password lacks digits or uppercase', () => {
      const result = registerSchema.safeParse({
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'jane.doe@payflow.local',
        currency: 'USD',
        password: 'lowercaseonly',
        confirmPassword: 'lowercaseonly',
        agreeTerms: true,
      });
      expect(result.success).toBe(false);
    });

    it('fails when terms of service are not accepted', () => {
      const result = registerSchema.safeParse({
        firstName: 'Jane',
        lastName: 'Doe',
        email: 'jane.doe@payflow.local',
        currency: 'USD',
        password: 'SecurePassword1',
        confirmPassword: 'SecurePassword1',
        agreeTerms: false,
      });
      expect(result.success).toBe(false);
      if (!result.success) {
        expect(result.error.issues[0].message).toContain('Terms of Service');
      }
    });
  });
});
