import type { AxiosError } from 'axios'

export type ResponseError = AxiosError

export enum ErrorType {
  Failure = 0,
  Validation = 1,
  NotFound = 2,
  Conflict = 3
}

export class AppError {
  public readonly message: string
  public readonly errorType: ErrorType
  public readonly details?: any // Optional property to hold extra data like problemDetails

  private constructor(message: string, errorType: ErrorType, details?: any) {
    this.message = message
    this.errorType = errorType
    this.details = details
  }

  public static notFound(message: string, details?: any): AppError {
    return new AppError(message, ErrorType.NotFound, details)
  }

  public static conflict(message: string, details?: any): AppError {
    return new AppError(message, ErrorType.Conflict, details)
  }

  public static validation(message: string, details?: any): AppError {
    return new AppError(message, ErrorType.Validation, details)
  }

  public static failure(message: string, details?: any): AppError {
    return new AppError(message, ErrorType.Failure, details)
  }
}
