import Box from '@mui/material/Box';
import { forwardRef } from 'react';

export const NativeDialog = forwardRef(function NativeDialog(props, ref) {
  return <Box component="dialog" ref={ref} {...props} />;
});
