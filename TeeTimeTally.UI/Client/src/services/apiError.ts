import type { ResponseError } from '@/primitives/error';
import { AppError } from '@/primitives/error';

export function mapApiErrorToAppError(error: ResponseError | any, fallbackMessage = 'An error occurred'): AppError {
  const apiError = error as ResponseError | undefined;
  try {
    const status = apiError?.response?.status;
    const data = apiError?.response?.data;

    if (status === 404) return AppError.notFound((data as any)?.detail || fallbackMessage, data);
    if (status === 409) return AppError.conflict((data as any)?.detail || fallbackMessage, data);
    if (status === 400 && (data as any)?.errors) return AppError.validation((data as any)?.errors || fallbackMessage, data);

    return AppError.failure((data as any)?.detail || apiError?.message || fallbackMessage, data);
  } catch (ex) {
    return AppError.failure(fallbackMessage);
  }
}
