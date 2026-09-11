import { Link as RouterLink } from 'react-router-dom';
import { ChevronRight } from 'lucide-react';
import Box from '@mui/material/Box';
import Card from '@mui/material/Card';
import CardActionArea from '@mui/material/CardActionArea';
import CardContent from '@mui/material/CardContent';
import Divider from '@mui/material/Divider';
import Link from '@mui/material/Link';
import List from '@mui/material/List';
import Paper from '@mui/material/Paper';
import Stack from '@mui/material/Stack';
import Typography from '@mui/material/Typography';
import { Trans, useTranslation } from '../../../i18n';

const card = { p: { xs: 3, sm: 4 } };
/** What the choice says on the left, where it goes on the right, and neither one crushing the other. */
const optionRow = { alignItems: 'center', justifyContent: 'space-between' };
const optionText = { minWidth: 0 };
const goesTo = { display: 'flex', flexShrink: 0, color: 'text.secondary' };

/**
 * One choice: an entire card that is a single link, so what a person reads and what they can press are the same
 * target rather than a title they have to hit exactly.
 *
 * `Card` and `CardActionArea` are the pair Material ships for a card that goes somewhere, and they are what the
 * previous composition was reimplementing by hand: the outlined `Paper` had `overflow: hidden` written on it only
 * to stop a `ListItemButton`'s ripple escaping the corner radius, which is a style `Card` already carries.
 */
function Choice({ to, title, detail }) {
  return (
    <Card component="li" variant="outlined">
      <CardActionArea component={RouterLink} to={to}>
        <CardContent>
          <Stack direction="row" spacing={2} sx={optionRow}>
            <Box sx={optionText}>
              {/* A heading inside a link is not a heading, and `subtitle1` becomes an `h6` unless the element is
                  said. This line is the link's own name, so it stays a span. */}
              <Typography component="span" variant="subtitle1">{title}</Typography>
              <Typography variant="body2" color="text.secondary">{detail}</Typography>
            </Box>
            {/* The arrow is the only thing on the row saying it leaves the page, and it is held out of the
                accessible tree on purpose: the name of this link is the choice, and a caller looks each choice
                up by a phrase inside that name. */}
            <Box aria-hidden sx={goesTo}><ChevronRight size={20} strokeWidth={2} /></Box>
          </Stack>
        </CardContent>
      </CardActionArea>
    </Card>
  );
}

/**
 * The choice a visitor makes before anything else: whether they are setting up their own account or a company's.
 * The two are different products for the same person, and nothing later can undo choosing wrongly on their behalf,
 * so the product asks rather than guesses (SPEC section 2.1).
 */
export function ChooseContextPage() {
  const { t } = useTranslation('identity');

  return (
    <Paper component="section" elevation={3} aria-labelledby="choose-context-heading" sx={card}>
      <Stack spacing={3}>
        <Typography id="choose-context-heading" component="h1" variant="h5">{t('register.choose.title')}</Typography>
        {/* Two of them is still a list, and it stays one: `List` carries the reset the hand-written `ul` was
            writing out in CSS, and `Stack` carries the gap between the cards. */}
        <Stack component={List} spacing={2} disablePadding>
          <Choice
            to="/personal/register"
            title={t('register.choose.personal.title')}
            detail={t('register.choose.personal.detail')}
          />
          <Choice
            to="/organizations/register"
            title={t('register.choose.organization.title')}
            detail={t('register.choose.organization.detail')}
          />
        </Stack>
        <Divider />
        <Typography variant="body2">
          <Trans
            i18nKey="identity:register.choose.alreadyHaveAccount"
            components={{ signIn: <Link component={RouterLink} to="/login" /> }}
          />
        </Typography>
      </Stack>
    </Paper>
  );
}
