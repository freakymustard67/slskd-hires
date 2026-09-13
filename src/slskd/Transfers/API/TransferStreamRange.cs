// <copyright file="TransferStreamRange.cs" company="JP Dillingham">
//           ▄▄▄▄     ▄▄▄▄     ▄▄▄▄
//     ▄▄▄▄▄▄█  █▄▄▄▄▄█  █▄▄▄▄▄█  █
//     █__ --█  █__ --█    ◄█  -  █
//     █▄▄▄▄▄█▄▄█▄▄▄▄▄█▄▄█▄▄█▄▄▄▄▄█
//   ┍━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━ ━━━━ ━  ━┉   ┉     ┉
//   │ Copyright (c) JP Dillingham.
//   │
//   │ This program is free software: you can redistribute it and/or modify
//   │ it under the terms of the GNU Affero General Public License as published
//   │ by the Free Software Foundation, version 3.
//   │
//   │ This program is distributed in the hope that it will be useful,
//   │ but WITHOUT ANY WARRANTY; without even the implied warranty of
//   │ MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//   │ GNU Affero General Public License for more details.
//   │
//   │ You should have received a copy of the GNU Affero General Public License
//   │ along with this program.  If not, see https://www.gnu.org/licenses/.
//   │
//   │ This program is distributed with Additional Terms pursuant to Section 7
//   │ of the AGPLv3.  See the LICENSE file in the root directory of this
//   │ project for the complete terms and conditions.
//   │
//   │ https://slskd.org
//   │
//   ├╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌╌ ╌ ╌╌╌╌ ╌
//   │ SPDX-FileCopyrightText: JP Dillingham
//   │ SPDX-License-Identifier: AGPL-3.0-only
//   ╰───────────────────────────────────────────╶──── ─ ─── ─  ── ──┈  ┈
// </copyright>

namespace slskd.Transfers.API
{
    using System;

    /// <summary>
    ///     A single satisfiable byte range over a resource of known total length.
    /// </summary>
    /// <param name="From">The first byte position, inclusive.</param>
    /// <param name="To">The last byte position, inclusive.</param>
    /// <param name="IsPartial">Whether the range is a proper subset of the resource.</param>
    public record StreamRange(long From, long To, bool IsPartial)
    {
        /// <summary>
        ///     Gets the number of bytes covered by the range.
        /// </summary>
        public long Length => To - From + 1;

        /// <summary>
        ///     Parses an HTTP Range header against the specified total length.
        /// </summary>
        /// <param name="header">The value of the Range header, or null if absent.</param>
        /// <param name="total">The total length of the resource in bytes.</param>
        /// <param name="range">The parsed range, if satisfiable.</param>
        /// <param name="unsatisfiable">Whether the header was present but not satisfiable.</param>
        /// <returns>True if a range was resolved (full resource when no header was given).</returns>
        public static bool TryParse(string header, long total, out StreamRange range, out bool unsatisfiable)
        {
            range = null;
            unsatisfiable = false;

            if (total <= 0)
            {
                unsatisfiable = true;
                return false;
            }

            if (string.IsNullOrWhiteSpace(header))
            {
                range = new StreamRange(0, total - 1, IsPartial: false);
                return true;
            }

            var value = header.Trim();

            if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase))
            {
                unsatisfiable = true;
                return false;
            }

            var spec = value.Substring("bytes=".Length);

            // multi-range requests are not supported
            if (spec.Contains(','))
            {
                unsatisfiable = true;
                return false;
            }

            var dash = spec.IndexOf('-');

            if (dash < 0)
            {
                unsatisfiable = true;
                return false;
            }

            var first = spec.Substring(0, dash).Trim();
            var last = spec.Substring(dash + 1).Trim();

            if (first.Length == 0)
            {
                // suffix range: the last N bytes
                if (!long.TryParse(last, out var suffix) || suffix <= 0)
                {
                    unsatisfiable = true;
                    return false;
                }

                var from = Math.Max(0, total - suffix);
                range = new StreamRange(from, total - 1, IsPartial: true);
                return true;
            }

            if (!long.TryParse(first, out var fromVal) || fromVal < 0)
            {
                unsatisfiable = true;
                return false;
            }

            long toVal;

            if (last.Length == 0)
            {
                toVal = total - 1;
            }
            else if (!long.TryParse(last, out toVal) || toVal < 0)
            {
                unsatisfiable = true;
                return false;
            }
            else
            {
                toVal = Math.Min(toVal, total - 1);
            }

            if (fromVal >= total || fromVal > toVal)
            {
                unsatisfiable = true;
                return false;
            }

            range = new StreamRange(fromVal, toVal, IsPartial: fromVal != 0 || toVal != total - 1);
            return true;
        }
    }
}
