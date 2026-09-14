// Forma que acepta el servidor para una razón y una referencia de retención.
// La pantalla la espeja para no mandar algo que ya sabe que va a ser rechazado;
// espeja y no reemplaza: la autoridad sigue siendo el servidor.
export const REFERENCE_FORMAT = /^[A-Za-z0-9._:-]{1,64}$/;

export const REFERENCE_SHAPE =
  "Letras, dígitos, punto, guion bajo, dos puntos o guion — de 1 a 64 caracteres.";

export const isAcceptedReference = (value: string): boolean => REFERENCE_FORMAT.test(value);
