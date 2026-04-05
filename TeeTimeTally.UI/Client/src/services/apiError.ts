import type { ResponseError } from '@/primitives/error';
import { AppError } from '@/primitives/error';

export function mapApiErrorToAppError(error: ResponseError | any, fallbackMessage = 'An error occurred'): AppError {
  const apiError = error as ResponseError | undefined;
  try {
    const status = apiError?.response?.status;
    const data = apiError?.response?.data;

    if (status === 404) return AppError.notFound((data as any)?.detail || fallbackMessage, data);
    if (status === 409) return AppError.conflict((data as any)?.detail || fallbackMessage, data);
    if (status === 400 && (data as any)?.errors) {
      const errors = (data as any).errors;
      // Try to extract a human-readable validation message for UI consumers.
      let message = fallbackMessage;
      try {
        if (Array.isArray(errors) && errors.length > 0) {
          message = typeof errors[0] === 'string' ? errors[0] : (errors[0].message || JSON.stringify(errors[0]));
        } else if (typeof errors === 'object') {
          const firstKey = Object.keys(errors)[0];
          const firstVal = errors[firstKey];
          if (Array.isArray(firstVal) && firstVal.length > 0) message = String(firstVal[0]);
          else message = String(firstVal ?? fallbackMessage);
        }
      } catch {
        message = fallbackMessage;
      }
      return AppError.validation(message, errors);
    }

    return AppError.failure((data as any)?.detail || apiError?.message || fallbackMessage, data);
  } catch (ex) {
    return AppError.failure(fallbackMessage);
  }
}
