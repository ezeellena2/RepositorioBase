import Box from '@mui/material/Box';
import Button from '@mui/material/Button';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { useState } from 'react';

const page = { maxWidth: 560 };
const header = { alignItems: 'flex-start', justifyContent: 'space-between', flexWrap: 'wrap' };
const supporting = { mt: 0.5, maxWidth: 640 };
// A section inside the shell, padded like every other one — the heavier card padding belongs to the public
// entrance, not to a panel sitting in the application. Home.jsx pads its sections identically.
const section = { p: { xs: 2, sm: 3 } };
const reading = { display: 'block', mt: 0.5 };

export function Counter() {
  const [count, setCount] = useState(0);

  return (
    <Stack component="section" aria-labelledby="counter-heading" spacing={3} sx={page}>
      <Stack direction="row" spacing={2} useFlexGap sx={header}>
        <Box>
          <Typography id="counter-heading" component="h1" variant="h5">Counter</Typography>
          <Typography variant="body2" color="text.secondary" sx={supporting}>
            This is a simple example of a React component.
          </Typography>
        </Box>
        <Button
          type="button"
          variant="contained"
          onClick={() => setCount((current) => current + 1)}
        >
          Increment
        </Button>
      </Stack>

      <Paper variant="outlined" sx={section}>
        {/* The live region announces the new total, and the number keeps an element of its own: both the reader
            of the region and the page object that locates `p[aria-live='polite'] strong` read it back. */}
        <Typography component="p" variant="body2" color="text.secondary" aria-live="polite">
          Current count:
          <Typography component="strong" variant="h4" color="text.primary" sx={reading}>{count}</Typography>
        </Typography>
      </Paper>
    </Stack>
  );
}
