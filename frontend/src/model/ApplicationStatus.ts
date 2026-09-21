export interface ApplicationStatus {
  application: string;
}

export function parseApplicationStatus(body: unknown): ApplicationStatus {
  if (typeof body !== 'object' || body === null ||
      !('application' in body) || typeof body.application !== 'string') {
    throw new Error('Invalid status response');
  }
  return { application: body.application };
}
