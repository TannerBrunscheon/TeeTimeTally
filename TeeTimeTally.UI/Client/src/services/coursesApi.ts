import { useHttpClient } from '@/composables/useHttpClient';
import type { Course, SearchCoursesRequest, CreateCourseRequest } from '@/models/course';

export async function searchCourses(params: SearchCoursesRequest): Promise<Course[]> {
  const { data } = await useHttpClient().get<Course[]>('/api/courses', { params });
  return data;
}

export async function getCourseById(courseId: string): Promise<Course> {
  const { data } = await useHttpClient().get<Course>(`/api/courses/${courseId}`);
  return data;
}

export async function createCourse(payload: CreateCourseRequest): Promise<Course> {
  const { data } = await useHttpClient().post<Course>('/api/courses', payload);
  return data;
}
