﻿using Download_Vouchery.Models;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Description;

namespace Download_Vouchery.Controllers
{
    public class BlobsController : ApiController
    {
        // Interface in place so you can resolve with IoC container of your choice
        private readonly IBlobService _service = new BlobService();

        private ApplicationDbContext db = new ApplicationDbContext();

        public UserManager<ApplicationUser> UserManager()
        {
            var manager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
            return manager;
        }

        public IQueryable<BlobUploadModel> GetBlobs()
        {
            var currentUser = UserManager().FindById(User.Identity.GetUserId());
            return db.BlobUploadModels.Where(o => o.FileOwner.Id == currentUser.Id);
        }

        /// <summary>
        /// Uploads one or more blob files.
        /// </summary>
        /// <returns></returns>
        [ResponseType(typeof(List<BlobUploadModel>))]
        public async Task<IHttpActionResult> PostBlobUpload()
        {
            try
            {
                // This endpoint only supports multipart form data
                if (!Request.Content.IsMimeMultipartContent("form-data"))
                {
                    return StatusCode(HttpStatusCode.UnsupportedMediaType);
                }

                // Call service to perform upload, then check result to return as content
                var result = await _service.UploadBlobs(Request.Content);
                if (result != null && result.Count > 0)
                {
                    return Ok(result);
                }

                // Otherwise
                return BadRequest();
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        private bool VoucherExists(Guid id)
        {
            return db.Vouchers.Count(e => e.VoucherId == id) > 0;
        }

        /// <summary>
        /// Downloads a blob file.
        /// </summary>
        /// <param name="blobId">The ID of the blob.</param>
        /// <returns></returns>
        public async Task<HttpResponseMessage> GetBlobDownload(Guid blobId, Guid voucherId)
        {
            var voucher = db.Vouchers.Find(voucherId);
            var blob = db.BlobUploadModels.Find(blobId);

            if (voucher == null)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            if (blob.FileId != voucher.VoucherFileId.FileId)
            {
                return new HttpResponseMessage(HttpStatusCode.Forbidden);
            }

            // IMPORTANT: This must return HttpResponseMessage instead of IHttpActionResult

            try
            {
                var result = await _service.DownloadBlob(blobId);
                if (result == null)
                {
                    return new HttpResponseMessage(HttpStatusCode.NotFound);
                }

                // Reset the stream position; otherwise, download will not work
                result.BlobStream.Position = 0;

                // Create response message with blob stream as its content
                var message = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StreamContent(result.BlobStream)
                };

                // Set content headers
                message.Content.Headers.ContentLength = result.BlobLength;
                message.Content.Headers.ContentType = new MediaTypeHeaderValue(result.BlobContentType);
                message.Content.Headers.ContentDisposition = new ContentDispositionHeaderValue("attachment")
                {
                    FileName = HttpUtility.UrlDecode(result.BlobFileName),
                    Size = result.BlobLength
                };

                return message;
            }
            catch (Exception ex)
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.InternalServerError,
                    Content = new StringContent(ex.Message)
                };
            }
        }
    }
}
