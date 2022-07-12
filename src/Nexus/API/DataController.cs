﻿using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Nexus.Services;
using System.ComponentModel.DataAnnotations;
using System.Net;

namespace Nexus.Controllers
{
    /// <summary>
    /// Provides access to data.
    /// </summary>
    [Authorize]
    [ApiController]
    [ApiVersion("1.0")]
    [Route("api/v{version:apiVersion}/[controller]")]
    internal class DataController : ControllerBase
    {
        // GET      /api/data

        #region Fields

        private IDataService _dataService;

        #endregion

        #region Constructors

        public DataController(
            IDataService dataService)
        {
            _dataService = dataService;
        }

        #endregion

        /// <summary>
        /// Gets the requested data.
        /// </summary>
        /// <param name="resourcePath">The path to the resource data to stream.</param>
        /// <param name="begin">Start date/time.</param>
        /// <param name="end">End date/time.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns></returns>

        [HttpGet]
        public async Task<ActionResult> GetStreamAsync(
            [BindRequired] string resourcePath,
            [BindRequired] DateTime begin,
            [BindRequired] DateTime end,
            [BindRequired] CancellationToken cancellationToken)
        {
            resourcePath = WebUtility.UrlDecode(resourcePath);
            begin = begin.ToUniversalTime();
            end = end.ToUniversalTime();

            try
            {
                var stream = await _dataService.ReadAsStreamAsync(resourcePath, begin, end, cancellationToken);

                Response.Headers.ContentLength = stream.Length;
                return File(stream, "application/octet-stream", "data.bin");
            }
            catch (ValidationException ex)
            {
                return UnprocessableEntity(ex.Message);
            }
            catch (Exception ex) when (ex.Message.StartsWith("Could not find resource path"))
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex) when (ex.Message.StartsWith("The current user is not permitted to access the catalog"))
            {
                return StatusCode(StatusCodes.Status403Forbidden, ex.Message);
            }
        }
    }
}
